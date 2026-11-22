# spec/providers.md — Provider 契约

- 版本：**1.0.0-draft**
- 状态：DRAFT（待独立评审）
- 起草日期：2026-11-22
- 适用：协议层（双端）、模型管理、能力矩阵、传输层

本文件定义两件事：(1) Canonical ChatRequest——上层唯一允许构造的请求形状；(2) 各家协议如何把它翻译成线上字节，以及如何把线上字节翻译回 `StreamEvent`（流式部分见 stream-events.md）。

**铁律**：领域层出现 `if (provider == "anthropic")` 即返工。所有差异必须被关在协议层的适配器内部。

---

## 1. Provider 配置模型

```
ProviderConfig {
  id, name,
  protocol: openai_chat | openai_responses | anthropic | gemini | openrouter | openai_compatible,
  base_url,                       // 用户可改；每协议有默认值
  org_id?,                        // OpenAI 组织
  keys: [KeyRef],                 // 1..N，见 ADR-002
  proxy: {mode: system|none|http|socks5, url?, auth?},
  connect_timeout_ms, read_idle_timeout_ms, max_retries,
  concurrency_limit,
  extra_headers: map<string,string>,
  extra_query: map<string,string>,
  extra_body_json?,               // 逃生舱：原样合并进请求体顶层
  pass_through: bool              // 跳过响应校验，只做最小解析（风险表里的"raw 模式"）
}
```

默认 `base_url`：

| protocol | 默认 |
| --- | --- |
| `openai_chat` | `https://api.openai.com/v1` |
| `openai_responses` | `https://api.openai.com/v1` |
| `anthropic` | `https://api.anthropic.com/v1` |
| `gemini` | `https://generativelanguage.googleapis.com/v1beta` |
| `openrouter` | `https://openrouter.ai/api/v1` |
| `openai_compatible` | 空，必填 |

路径拼接规则（MUST 统一实现，双端一致）：
- 若 `base_url` 以 `/` 结尾则去掉尾部 `/`。
- 若 `base_url` 已包含该协议的标准后缀（`/chat/completions`、`/messages`、`/responses`），MUST 直接使用，不再拼接。
- 否则拼接：`openai_chat`/`openai_compatible`/`openrouter` → `/chat/completions`；`openai_responses` → `/responses`；`anthropic` → `/messages`；`gemini` → `/models/{model}:streamGenerateContent`。
- 若用户填的 URL 指向网页（以 `/` 结尾且无版本段）→ 不报错，但返回 `notice(provider_downgraded)`? 否：交由 `ENDPOINT_NOT_FOUND` 呈现，并在诊断页提供"协议自动探测"。

## 2. Canonical ChatRequest

```json
{
  "model": "claude-sonnet-5-5",
  "system": "You are ...",
  "messages": [
    {"role": "user", "parts": [
      {"type": "text", "text": "这张图里是什么？"},
      {"type": "image", "mime": "image/png", "data_ref": "att_01H...", "detail": "auto"}
    ]},
    {"role": "assistant", "parts": [
      {"type": "text", "text": "是猫。"},
      {"type": "tool_call", "id": "call_a1", "name": "web_search", "args": {"q": "猫"}}
    ]},
    {"role": "tool", "tool_call_id": "call_a1", "parts": [
      {"type": "tool_result", "ok": true, "result": {"items": []}}
    ]}
  ],
  "tools": [{"name": "web_search", "description": "...", "parameters": {"type": "object", "properties": {}}}],
  "tool_choice": "auto",
  "sampling": {"temperature": 0.7, "top_p": 1.0, "top_k": null, "max_output_tokens": 2048,
               "stop": ["\n\nHuman:"], "seed": null,
               "frequency_penalty": null, "presence_penalty": null},
  "reasoning": {"mode": "off|effort|budget", "effort": "low|medium|high", "budget_tokens": null,
                "include_summary": false},
  "response_format": {"type": "text|json_object|json_schema", "schema": null},
  "cache": {"breakpoints": [2, 6]},
  "stream": true,
  "client_request_id": "0193b7c1-...",
  "extra": {"headers": {}, "query": {}, "body": {}}
}
```

规则：

1. 部分类型闭集：`text` | `image` | `file` | `tool_call` | `tool_result`。新增需升版本。
2. `data_ref` 指向本地附件表（`attachment.id`），协议层负责读取字节并编码；**禁止**在 canonical 层放 base64。
3. `sampling` 的 `null` 表示"未指定，用 provider 默认"，与"显式设为 0"必须区分。
4. 请求快照：请求发出前序列化为不可变字节（ADR-003 §3），重试复用该字节。
5. `extra` 是唯一逃生舱；只有 `pass_through=true` 的 provider 允许使用，且使用时 UI 必须提示"可能在升级后失效"。

## 3. 各家请求映射

### 3.1 公共

| canonical | openai_chat / openai_compatible / openrouter | openai_responses | anthropic | gemini |
| --- | --- | --- | --- | --- |
| `system` | `messages[0]={role:"system",content}` | `instructions` | 顶层 `system`（字符串或 block 数组） | `systemInstruction.parts[].text` |
| 文本 part | `content` 为字符串（纯文本时）或 `[{type:"text",text}]` | `input[]` 的 `input_text` | `{type:"text",text}` | `{text}` |
| 图片 part | `{type:"image_url",image_url:{url:"data:<mime>;base64,<b64>",detail}}` | `{type:"input_image",image_url}` | `{type:"image",source:{type:"base64",media_type,data}}` | `{inlineData:{mimeType,data}}` |
| 文件 part（PDF） | **不支持**，M3 阶段本地抽取为文本并 `notice(capability_stripped)` | 同 | `{type:"document",source:{type:"base64",media_type:"application/pdf",data}}` | `{inlineData:{mimeType:"application/pdf",data}}` |
| 工具定义 | `tools:[{type:"function",function:{name,description,parameters}}]` | `tools:[{type:"function",name,description,parameters}]` | `tools:[{name,description,input_schema}]` | `tools:[{functionDeclarations:[{name,description,parameters}]}]` |
| `tool_choice` | `auto`/`none`/`required`/`{type:"function",function:{name}}` | `auto`/`none`/`required`/`{type:"function",name}` | `{type:"auto"}`/`{type:"any"}`/`{type:"tool",name}` | `toolConfig.functionCallingConfig:{mode:AUTO|NONE|ANY, allowedFunctionNames?}` |
| 工具调用（assistant） | `tool_calls:[{id,type:"function",function:{name,arguments:<json string>}}]` | `{type:"function_call",call_id,name,arguments}` | `{type:"tool_use",id,name,input:<object>}` | `{functionCall:{name,args}}`（无 id） |
| 工具结果 | `{role:"tool",tool_call_id,content:<string>}` | `{type:"function_call_output",call_id,output}` | `{role:"user",content:[{type:"tool_result",tool_use_id,content}]}` | `{role:"user",parts:[{functionResponse:{name,response}}]}` |
| `temperature` | 同 | 同 | 同（thinking 开启时必须 1） | `generationConfig.temperature` |
| `top_p` | 同 | 同 | 同（与 temperature 互斥：只发一个） | `generationConfig.topP` |
| `top_k` | 不支持 → 剥离 | 不支持 → 剥离 | 同 | `generationConfig.topK` |
| `max_output_tokens` | `max_tokens`（新模型用 `max_completion_tokens`，由能力矩阵决定） | `max_output_tokens` | `max_tokens`（**必填**，缺省取模型默认） | `generationConfig.maxOutputTokens` |
| `stop` | `stop` | 不支持 → 剥离 | `stop_sequences` | `generationConfig.stopSequences` |
| `seed` | `seed` | 不支持 → 剥离 | 不支持 → 剥离 | `generationConfig.seed` |
| `frequency_penalty` | 同 | 同 | 不支持 → 剥离 | `generationConfig.frequencyPenalty` |
| `presence_penalty` | 同 | 同 | 不支持 → 剥离 | `generationConfig.presencePenalty` |
| `reasoning.effort` | `reasoning_effort` | `reasoning:{effort,summary:"auto"}` | 由 `budget` 表达（effort 映射见 3.4） | `thinkingConfig.thinkingBudget` |
| `reasoning.budget_tokens` | 忽略 → 映射 effort | `reasoning.effort` 估计 | `thinking:{type:"enabled",budget_tokens}` | `thinkingConfig.thinkingBudget` |
| `response_format=json_schema` | `response_format:{type:"json_schema",json_schema:{name,schema,strict:true}}` | `text:{format:{type:"json_schema",name,schema}}` | 不支持 → 剥离 + `notice` | `generationConfig:{responseMimeType:"application/json",responseSchema}` |
| `cache.breakpoints` | 无操作（自动前缀缓存）+ `notice` | 同 | 在对应内容块加 `cache_control:{type:"ephemeral"}` | 无操作（cachedContent 需单独创建，v1 不做） |

**剥离规则**：任何被剥离的字段 MUST 产生 `notice(capability_stripped, {dropped:[...]})`，UI MUST 在消息气泡内以可读文案呈现（如"该模型不支持 seed 参数，已忽略"）。静默丢失是缺陷。

### 3.2 OpenAI Chat Completions / 兼容层

```http
POST {base}/chat/completions
Authorization: Bearer <key>
Content-Type: application/json
Accept: text/event-stream
```

body 关键字段：`model, messages, stream:true, stream_options:{include_usage:true}, temperature, top_p, max_tokens|max_completion_tokens, stop, seed, frequency_penalty, presence_penalty, response_format, tools, tool_choice, reasoning_effort`。

兼容层的差异（MUST 有单元测试覆盖）：
- 若端点返回 400 且 message 提到 `stream_options` / `unsupported parameter` → 去掉该字段重发一次（只允许一次降级重试），并记 trace `compat_degraded=stream_options`。
- 若端点返回 400 且 message 提到 `max_completion_tokens` → 换回 `max_tokens` 重发一次。
- 上述两次降级重试**不计入** `max_retries`，但每类只能发生一次/请求。
- `Authorization` 头可由 `extra_headers` 覆盖（自建网关常用 `x-api-key` 或 `api-key`）；覆盖后 MUST 不再发送 `Authorization`。

### 3.3 OpenAI Responses

```http
POST {base}/responses
Authorization: Bearer <key>
```

body：`model, instructions, input, tools, tool_choice, temperature, top_p, max_output_tokens, reasoning{effort}, text{format}, stream:true`。

注意：Responses 的 `input` 是无状态数组，v1 MUST NOT 使用 `previous_response_id`（无状态更适合本地会话树）。

### 3.4 Anthropic Messages

```http
POST {base}/messages
x-api-key: <key>
anthropic-version: 2023-06-01
anthropic-beta: <按能力矩阵开启，如 prompt-caching / interleaved-thinking>
```

- `max_tokens` 必填。
- thinking 开启时：MUST 强制 `temperature=1`，MUST 不再发送 `top_p`/`top_k`，并 `notice(provider_downgraded)`（原因：thinking 与采样参数互斥）。
- `reasoning.effort` → `budget_tokens` 映射：`low`=1024，`medium`=4096，`high`=16384，上限取 `model.max_output` 的 80%。
- 工具结果必须包在 `role:"user"` 的 `tool_result` 块里；若 canonical 中连续多个 `role:"tool"`，MUST 合并成一条 user 消息的多个 `tool_result` 块。
- 空文本块 MUST 过滤（Anthropic 会 400）。

### 3.5 Gemini generateContent

```http
POST {base}/models/{model}:streamGenerateContent?alt=sse&key=<key>
```
或头方式 `x-goog-api-key: <key>`（优先头方式，避免 key 出现在 URL 日志里）。

- 角色映射：`user`→`user`，`assistant`→`model`。MUST 合并连续同角色消息（Gemini 要求交替）。`system` 走 `systemInstruction`。
- `functionResponse.response` 必须是对象；若工具结果是字符串，MUST 包成 `{"result": "..."}`。
- `finishReason=SAFETY` 且已有部分文本 → `done{content_filter}`，不报错。
- 请求 URL 含 `key` 时 MUST 在 trace 中脱敏。

### 3.6 OpenRouter

同 3.2（OpenAI Chat），额外头（可选，便于控制台归因）：
```
HTTP-Referer: https://<用户配置的站点，可空>
X-Title: NeoChat
```
v1 MUST NOT 发送任何可识别用户的字段。`extra_body_json` 可传 `provider:{order,allow_fallbacks}`，但必须由用户显式编辑（不内置默认，避免影响计费）。

## 4. 模型列表与元数据

| protocol | 列表接口 | 字段 |
| --- | --- | --- |
| openai_chat / openai_compatible | `GET {base}/models` | `data[].id`（部分端点无此接口 → 404 回退内置清单） |
| openai_responses | 同（用 chat 列表） | 同上 |
| anthropic | `GET {base}/models?limit=1000` | `data[].id, display_name, created_at` |
| gemini | `GET {base}/models?pageSize=1000` | `models[].name, displayName, inputTokenLimit, outputTokenLimit, supportedGenerationMethods` |
| openrouter | `GET {base}/models` | `data[].id, context_length, pricing{prompt,completion}, architecture.input_modalities` |

规则：
- 列表拉取失败 → 使用内置清单（随版本更新）+ 保留用户手动添加项，并 `notice` 说明"模型列表来自本地缓存"。
- 合并策略：远端列表 ∪ 内置清单 ∪ 用户手动项；同名以远端为准（但用户手改过的字段 MUST 保留，标 `pinned_by_user=true`）。
- 所有元数据本地可编辑：`context_window, max_output, vision, tools, thinking, cache, price_input, price_output, price_cache_read, price_cache_write, currency`。

## 5. Token 计数

| protocol | 方式 | 误差 |
| --- | --- | --- |
| anthropic | `POST {base}/messages/count_tokens`（准确） | 0 |
| gemini | `POST {base}/models/{model}:countTokens`（准确） | 0 |
| openai_chat / responses / openrouter / 兼容 | 无公开计数接口 → 本地分词器估算 | **英数混合 ±5%，中文为主 ±15%**（须在 UI 标注 `estimated=true` 与 `error_bound_pct`） |

本地分词器选择（M2 决策项，需 ADR）：Android `com.kmnv.jtokkit` 或同类；Windows `Microsoft.ML.Tokenizers`。两者 MUST 对同一测试语料输出相同 token 数（偏差 >1% 即返工）。

## 6. 能力矩阵与自动降级

每个模型有布尔能力：`vision, tools, thinking, cache, json_schema, long_context`。请求构造时逐项检查：

| 缺失能力 | 行为 |
| --- | --- |
| `vision` 但请求含图片 | 剥离图片 part，`notice(capability_stripped,{dropped:["vision"]})`，并在消息气泡中显示"已忽略 N 张图片（该模型不支持图片输入）" |
| `tools` 但请求含工具 | 剥离 `tools`/`tool_choice`，同上通知 |
| `json_schema` 但请求为结构化输出 | 降级为 `json_object`（若支持）或纯文本 + 在提示词前置约束，并通知 |
| `thinking` 但请求开启推理 | 剥离推理参数，`notice(reasoning_unsupported)` |
| 上下文超窗 | 不自动截断，返回 `CONTEXT_OVERFLOW` + `reduce_context` 操作（自动截断会让用户丢失信息且不可预期） |

## 7. 传输层契约

| 项 | 规则 |
| --- | --- |
| TLS | 最低 TLS 1.2；禁止关闭证书校验（不提供"忽略证书错误"开关） |
| 重定向 | 流式 POST MUST NOT 自动跟随 3xx；3xx 映射为 `ENDPOINT_NOT_FOUND`（防凭证被转发到第三方） |
| 压缩 | `Accept-Encoding: gzip`；不支持 br（实现差异风险） |
| UA | `NeoChat/<version> (<platform>; <os_version>)`，不含设备唯一标识 |
| 请求体 | UTF-8，无 BOM |
| 代理 | `system` 读系统代理；`http`/`socks5` 用显式配置。SOCKS5 认证失败 → `NET_PROXY_AUTH_FAILED` |
| 连接复用 | 允许 HTTP/2 与连接池；同一 provider 复用 `HttpClient`（Windows 必须用 `SocketsHttpHandler` 且 `PooledConnectionLifetime` 合理） |
| 超时 | 见 stream-events §2.2 |
| 取消 | 见 ADR-003 |
| 响应头记录 | 只记 `x-request-id`、`request-id`、`anthropic-ratelimit-*`、`x-ratelimit-*`、`retry-after`。禁止记录 `set-cookie` |

## 8. 双端一致性判据

同一个 `ChatRequest` + 同一个 `ProviderConfig` 在两端构造出的 HTTP 请求必须满足：

1. method 与 path 相同（path 中的 model 段也相同）。
2. 语义等价 body：JSON 键集合相同、值相同（键顺序不计）、数字类型一致（不得一端发 `0.7` 一端发 `"0.7"`）。
3. 头集合相同（顺序与大小写不计），`Authorization`/`x-api-key` 的值相同。
4. 二进制 body 的规范化 JSON（键排序、无空白）SHA-256 相同。

以上由 M0-QE-002 的 `request-golden` 用例机器判定，作为 G1 的一部分。

## 9. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 首版 |
