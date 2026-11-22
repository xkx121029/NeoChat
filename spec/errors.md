# spec/errors.md — 统一错误模型

- 版本：**1.0.0-draft**
- 状态：DRAFT（待独立评审）
- 起草日期：2026-11-22
- 适用：双端协议层、传输层、状态层、错误页、诊断页、崩溃兜底

**核心约束**：任何一个错误都必须是"发生了什么事（人话）+ 可能原因（≤3，按可能性排序）+ 下一步（可点操作）"。只抛错误码或原始 JSON 视为缺陷。用户自救能力是产品的最后一道防线，因此这条约束从契约层开始，不是 M7 的收尾工作。

---

## 1. 阶段（stage，闭集）

| stage | 含义 |
| --- | --- |
| `config` | 请求构造前的本地校验（Key 缺失、Base URL 非法、模型不支持某能力） |
| `resolve` | DNS、代理选择、证书准备 |
| `connect` | TCP/TLS 建连 |
| `send` | 写入请求体 |
| `first_byte` | 等待响应头/首字节 |
| `stream_read` | 读流（含 idle 超时、连接重置） |
| `parse` | SSE 分帧与事件归一化 |
| `provider` | provider 明确返回的错误（HTTP 状态或错误事件） |
| `tool` | 工具/MCP 执行 |
| `persist` | 落盘（DB 写失败、磁盘满） |
| `crypto` | 密钥库/DPAPI/解密相关 |

## 2. 错误对象（线格式 = StreamEvent.failed.error）

```json
{
  "code": "AUTH_INVALID_KEY",
  "stage": "provider",
  "provider": "openai_compatible",
  "model": "deepseek-chat",
  "http_status": 401,
  "provider_code": "invalid_api_key",
  "retryable": false,
  "retry_after_ms": null,
  "title_key": "errors.AUTH_INVALID_KEY.title",
  "params": {"hint": "sk-…a1b2"},
  "causes": ["errors.AUTH_INVALID_KEY.cause.revoked", "errors.AUTH_INVALID_KEY.cause.wrong_endpoint", "errors.AUTH_INVALID_KEY.cause.truncated_paste"],
  "actions": [{"a": "edit_provider"}, {"a": "copy_diagnostics"}],
  "raw": "{\"error\":{\"message\":\"Incorrect API key provided: sk-…a1b2\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}",
  "request_id": "req_8f3a",
  "trace_id": "0193b7c1-…"
}
```

字段规则：

| 字段 | 规则 |
| --- | --- |
| `code` | 必填，见 §3 闭集。新增 code 需 spec 升版本 |
| `stage` | 必填，见 §1 |
| `provider` / `model` | 可选；本地错误可缺 |
| `http_status` | 可选；无 HTTP 层时缺 |
| `provider_code` | 可选；provider 自己的错误码/类型，原文保留 |
| `retryable` | 必填；指"同一请求可自动重试" |
| `retry_after_ms` | 可选；来自 `Retry-After`，上限 30000 |
| `title_key` | 必填，见 §6 |
| `params` | 必填（可为空对象）；文案插值用 |
| `causes` | 必填，1~3 条，按可能性降序。key 形式，渲染为文本 |
| `actions` | 必填，≥1 条。见 §4。UI MUST 全部可点 |
| `raw` | 可选；脱敏后截断 ≤512 **字节**（按 UTF-8 边界截断，不切坏多字节字符），且 MUST NOT 含 Authorization/Cookie/API Key |
| `request_id` | 可选；provider 返回的请求 id（OpenAI `x-request-id`、Anthropic `request-id`），用于向 provider 报障 |
| `trace_id` | 必填；本地 UUIDv7，用于关联本地 trace 文件 |

## 3. 错误码表（闭集）

### 3.1 网络与传输

| code | stage | retryable | 中文标题 | 候选原因 | 可点操作 |
| --- | --- | --- | --- | --- | --- |
| `NET_OFFLINE` | resolve | ✅ | 设备当前没有网络连接 | 飞行模式/无信号<br>系统网络未就绪<br>VPN 断开 | 重试<br>打开网络设置 |
| `NET_DNS_FAILED` | resolve | ✅ | 无法解析服务器域名 | Base URL 拼写错误<br>DNS 被劫持或不可用<br>网络需要代理 | 检查 Base URL<br>检查代理<br>重试 |
| `NET_CONNECT_TIMEOUT` | connect | ✅ | 连接服务器超时 | 网络过慢<br>被网络环境阻断<br>代理配置错误 | 重试<br>检查代理 |
| `NET_CONNECT_REFUSED` | connect | ✅ | 服务器拒绝了连接 | Base URL 端口错误<br>本地服务（Ollama 等）未启动<br>代理未运行 | 检查 Base URL<br>检查代理<br>重试 |
| `NET_TLS_FAILED` | connect | ❌ | 安全连接建立失败 | 系统时间不正确<br>代理在进行证书拦截<br>服务器证书过期 | 检查系统时间<br>检查代理<br>复制诊断 |
| `NET_PROXY_AUTH_FAILED` | connect | ❌ | 代理认证失败 | 代理用户名或密码错误<br>代理协议选错（HTTP/SOCKS5） | 修改代理设置 |
| `NET_CONNECTION_RESET` | stream_read | ✅（仅首字节前） | 连接被中途断开 | 网络切换（Wi-Fi/4G）<br>代理或防火墙中断<br>服务端断开长连接 | 重试<br>检查代理 |
| `NET_READ_TIMEOUT` | stream_read | ✅（仅首字节前） | 服务器长时间没有返回数据 | 模型排队或过载<br>代理缓冲了响应<br>请求过大 | 重试<br>切换模型<br>检查代理 |

### 3.2 鉴权与配额

| code | stage | retryable | 中文标题 | 候选原因 | 可点操作 |
| --- | --- | --- | --- | --- | --- |
| `AUTH_MISSING_KEY` | config | ❌ | 这个 Provider 还没有配置密钥 | 首次使用未填写<br>密钥被删除<br>多个 Key 全部禁用 | 填写密钥 |
| `AUTH_INVALID_KEY` | provider | ❌ | 密钥无效或已被撤销 | 密钥被撤销/重置<br>密钥与端点不匹配（把 A 家的 Key 填进了 B 家）<br>粘贴时被截断或含空格 | 修改密钥<br>检查 Base URL<br>复制诊断 |
| `AUTH_INSUFFICIENT_QUOTA` | provider | ❌ | 账户余额或配额不足 | 预付费余额用尽<br>免费额度已过期<br>组织配额被管理员限制 | 去 Provider 控制台充值<br>切换模型<br>换一个 Key |
| `RATE_LIMITED` | provider | ✅ | 请求太频繁，被限流 | 每分钟请求数超限<br>并发请求过多<br>该 Key 的每日额度用尽 | {retry_after} 秒后重试<br>降低并发<br>切换 Key |

### 3.3 请求与模型

| code | stage | retryable | 中文标题 | 候选原因 | 可点操作 |
| --- | --- | --- | --- | --- | --- |
| `MODEL_NOT_FOUND` | provider | ❌ | 该模型在服务器上不存在 | 模型已下线或改名<br>模型 ID 拼写错误<br>该 Base URL 不提供此模型 | 刷新模型列表<br>手动指定模型 |
| `PARAM_INVALID` | provider | ❌ | 请求参数不被接受 | 该模型不支持某个采样参数<br>数值越界（如 temperature > 2）<br>合并了不同协议所需的字段 | 查看详情<br>重置为默认参数<br>切换模型 |
| `CONTEXT_OVERFLOW` | provider | ❌ | 上下文长度超过模型上限 | 历史消息太多<br>附件太大<br>模型窗口被误设 | 修剪上下文<br>移除附件<br>切换长窗口模型 |
| `CAPABILITY_UNSUPPORTED` | config | ❌ | 该模型不支持此内容类型 | 模型不支持图片输入<br>模型不支持工具调用<br>模型不支持结构化输出 | 切换模型<br>移除图片/工具<br>查看能力矩阵 |
| `CONTENT_FILTERED` | provider | ❌ | Provider 的内容策略拦截了本次请求或回复 | 输入触发了服务商策略<br>输出被中途拦截<br>账户所在区域限制 | 修改输入后重试<br>切换模型 |
| `PARSE_SSE_INVALID` | parse | ❌ | 服务器返回的数据格式无法识别 | Base URL 指向了非流式端点<br>代理注入了 HTML 错误页<br>该端点返回的是 JSON 错误 | 检查 Base URL 是否为同一协议<br>关闭代理重试<br>查看原始响应 |
| `PARSE_INCOMPLETE` | parse | ✅ | 回复在中途被截断 | 网络中断<br>服务端提前关闭<br>代理缓冲截断 | 重试<br>检查代理 |
| `PARSE_EVENT_TOO_LARGE` | parse | ❌ | 单条流数据超出安全上限（16 MiB） | 端点返回了非流式大响应<br>代理重复注入数据 | 检查 Base URL<br>查看原始响应 |

### 3.4 服务端

| code | stage | retryable | 中文标题 | 候选原因 | 可点操作 |
| --- | --- | --- | --- | --- | --- |
| `SERVER_ERROR` | provider | ✅ | 服务器内部错误（{http_status}） | Provider 侧故障<br>模型正在重启<br>该区域服务降级 | 重试<br>切换模型<br>查看服务状态 |
| `SERVER_OVERLOADED` | provider | ✅ | 服务器过载，暂时无法处理 | 高峰期排队<br>账户被降级<br>区域容量不足 | {retry_after} 秒后重试<br>切换模型 |
| `ENDPOINT_NOT_FOUND` | provider | ❌ | 端点地址不存在（404） | Base URL 缺少或多了 `/v1`<br>协议选错（OpenAI 兼容 vs Anthropic）<br>该 Path 需要自定义 | 检查 Base URL<br>切换协议<br>查看文档 |
| `UNKNOWN_PROVIDER_ERROR` | provider | ❌ | Provider 返回了未识别的错误 | 该服务商改了错误格式<br>中转服务自定义了错误<br>响应被代理改写 | 查看原始响应<br>复制诊断<br>切换模型 |

### 3.5 本地

| code | stage | retryable | 中文标题 | 候选原因 | 可点操作 |
| --- | --- | --- | --- | --- | --- |
| `CONFIG_INVALID_BASE_URL` | config | ❌ | Base URL 格式不正确 | 缺少 `https://`<br>含空格或中文标点<br>指向了网页地址而不是 API 端点 | 修改 Base URL<br>查看示例 |
| `KEY_STORE_UNAVAILABLE` | crypto | ❌ | 无法访问系统密钥库 | 设备锁屏凭据已变更<br>系统密钥库被重置<br>StrongBox 不可用且回退失败 | 重新录入密钥<br>查看诊断 |
| `DB_WRITE_FAILED` | persist | ❌ | 数据保存失败 | 磁盘空间不足<br>数据库被占用或损坏<br>存储权限被系统回收 | 清理空间<br>数据库自检 |
| `TOOL_EXEC_FAILED` | tool | ❌ | 工具执行失败 | 工具服务不可达<br>参数不符合工具 schema<br>MCP 服务器进程已退出 | 重试工具<br>查看工具详情<br>检查 MCP 服务器 |
| `INTERNAL` | — | ❌ | 出现了未预期的内部错误 | 应用内部缺陷<br>数据状态不一致 | 复制诊断<br>重试 |

**新增 code 的规则**：只有当现有 code 无法在不损失可操作性的前提下表达时才新增；新增必须同时提供三种语言文案与至少一条 action，并在 `spec/CHANGELOG.md` 记录。

## 4. 操作（action，闭集）

| `a` | 语义 | 参数 |
| --- | --- | --- |
| `retry` | 用同一请求快照重试 | — |
| `retry_after` | 倒计时后自动重试 | `{ms}` |
| `edit_provider` | 打开该 Provider 编辑页（Key/Base URL 高亮） | `{focus:"key"\|"base_url"}` |
| `open_provider_settings` | 打开 Provider 列表 | — |
| `manage_keys` | 打开多 Key 管理 | — |
| `check_proxy` | 打开代理设置并跑连通性测试 | — |
| `check_base_url` | 打开 Base URL 诊断（协议自动探测） | — |
| `switch_model` | 打开模型选择器（过滤掉不具备所需能力的模型） | `{require:["vision"\|"tools"\|"long_context"]}` |
| `reduce_context` | 打开上下文修剪面板并预选"保留最近 N 轮" | — |
| `top_up` | 用系统浏览器打开 provider 计费页（URL 由 provider 配置提供，不内置硬编码） | — |
| `open_docs` | 打开内置帮助页 | `{topic}` |
| `view_raw` | 查看脱敏后的原始响应 | — |
| `copy_diagnostics` | 复制诊断段（脱敏）到剪贴板 | — |
| `retry_tool` | 重试单个工具调用 | `{index}` |
| `db_check` | 打开数据库自检与修复 | — |

## 5. Provider 错误映射

### 5.1 OpenAI / OpenAI 兼容 / OpenRouter

| 条件 | code |
| --- | --- |
| 401 | `AUTH_INVALID_KEY` |
| 403 + body 含 `country`/`region` | `CONTENT_FILTERED`（原因：区域限制） |
| 403 其他 | `AUTH_INVALID_KEY` |
| 404 + `model` 相关 message | `MODEL_NOT_FOUND` |
| 404 其他 | `ENDPOINT_NOT_FOUND` |
| 429 + `code=insufficient_quota` | `AUTH_INSUFFICIENT_QUOTA` |
| 429 其他 | `RATE_LIMITED`（解析 `Retry-After`） |
| 400 + `context_length_exceeded` | `CONTEXT_OVERFLOW` |
| 400 + `content_filter`/`content_policy` | `CONTENT_FILTERED` |
| 400 + `invalid_request_error` 含参数名 | `PARAM_INVALID`（`params.field` 保留原文参数名） |
| 400 其他 | `PARAM_INVALID` |
| 413 | `CONTEXT_OVERFLOW` |
| 500/502/503/504 | `SERVER_ERROR` |
| 529 | `SERVER_OVERLOADED` |
| 响应头 `x-should-retry: true` | 覆盖 `retryable=true` |

### 5.2 OpenAI Responses

同 5.1，另外：`error.type` = `invalid_request_error` 且 message 含 `previous_response_id` → `PARAM_INVALID`（v1 不实现会话续接，见 stream-events §7）。

### 5.3 Anthropic

| `error.type` | code |
| --- | --- |
| `authentication_error` | `AUTH_INVALID_KEY` |
| `permission_error` | `AUTH_INVALID_KEY`（原因：密钥无该模型权限） |
| `rate_limit_error` | `RATE_LIMITED` |
| `insufficient_quota` / 402 | `AUTH_INSUFFICIENT_QUOTA` |
| `invalid_request_error` 且 message 含 `prompt is too long` | `CONTEXT_OVERFLOW` |
| `invalid_request_error` 其他 | `PARAM_INVALID` |
| `overloaded_error` | `SERVER_OVERLOADED` |
| `api_error` | `SERVER_ERROR` |
| `request_too_large` | `CONTEXT_OVERFLOW` |
| `not_found_error` | `MODEL_NOT_FOUND` |

### 5.4 Gemini

| 条件 | code |
| --- | --- |
| 400 + `API_KEY_INVALID` / `API key not valid` | `AUTH_INVALID_KEY` |
| 400 + `INVALID_ARGUMENT` 含 `token count`/`too long` | `CONTEXT_OVERFLOW` |
| 400 其他 | `PARAM_INVALID` |
| 403 | `AUTH_INVALID_KEY`（原因：项目未启用该 API 或区域限制） |
| 404 | `MODEL_NOT_FOUND` |
| 429 `RESOURCE_EXHAUSTED` | `RATE_LIMITED` |
| 500/503 `INTERNAL`/`UNAVAILABLE` | `SERVER_ERROR` |
| `promptFeedback.blockReason` | `CONTENT_FILTERED` |
| `finishReason=SAFETY` | `done{content_filter}`（**不是** failed——已有部分内容） |

## 6. 文案与本地化

- key 约定：`errors.<CODE>.title` / `errors.<CODE>.cause.<slug>` / `errors.<CODE>.action.<a>`。
- 三语齐备（zh-CN / zh-Hant / en），CI 静态检查缺失 key 即失败。
- 文案硬约束：
  1. 标题 ≤ 24 个汉字（英文 ≤ 10 词），陈述事实，不道歉、不卖萌、不使用"哦""啦"。
  2. 每条原因 ≤ 20 个汉字，必须具体到可检查的对象（"Base URL 缺少 /v1"而不是"配置有问题"）。
  3. 操作按钮 ≤ 6 个汉字（英文 ≤ 3 词），总是动词开头。
  4. 禁止出现未脱敏的 Key、完整 URL 查询串、用户消息正文。
- 未知 provider 错误：标题固定为 `UNKNOWN_PROVIDER_ERROR`，`raw` 必须可查看，且必须给 `copy_diagnostics`。

## 7. 脱敏规则（双端同一实现，有单元测试）

| 目标 | 规则 |
| --- | --- |
| `Authorization` 头、`x-api-key`、`api-key`、query 中的 `key`/`api_key`/`access_token` | 整体替换为 `[redacted]` |
| 任何形如 provider key 的串（`sk-[A-Za-z0-9_-]{16,}`、`AIza[0-9A-Za-z_-]{20,}`、`sk-ant-[A-Za-z0-9_-]{16,}`、`gsk_[A-Za-z0-9]{20,}`、`xai-[A-Za-z0-9]{16,}`） | 保留首 4 + `…` + 尾 4 |
| `Cookie` 头 | `[redacted]` |
| `Set-Cookie` | `[redacted]` |
| 邮箱（用户可能把邮箱当组织 ID） | 保留首字符 + `***` + 域名 |
| 本地绝对路径中的用户名 | 替换为 `~` |
| 截断 | ≤512 字节，按 UTF-8 字符边界 |

脱敏函数 `Redactor.redact(s)` 双端各一份，有共享测试向量文件 `spec/conformance/redaction-vectors.jsonl`（M0-QE-002 产出）。同一输入必须得到同一输出。

## 8. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 首版：28 个 code、14 个 action、四家映射、脱敏规则 |
