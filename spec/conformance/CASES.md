# spec/conformance/CASES.md — conformance 用例集

- 版本：**1.0.1**
- 状态：**FROZEN**（2026-11-22；四轮独立评审关闭，见 docs/reviews/spec-review-1~4.md）
- 起草日期：2026-11-22
- 用例总数：**78**

这些用例是协议层的裁判。任一用例失败即返工，不接受"这个样本太少见了"的辩护——少见正是它要防的东西。

## 0. Runner 契约

| 项 | 约定 |
| --- | --- |
| 样本存放 | `spec/conformance/samples/<CASE-ID>/` |
| 样本形态（网络层） | `stream.sse`（原始 HTTP body 字节，未解压）+ `meta.json`（HTTP 状态、头、协议、config、终止原因）。`meta.json` 可选带 `content_encoding: "gzip"`，此时 `stream.sse` 为压缩字节，用于验收解压分支（FRAME-013） |
| 切包脚本 | `meta.json` 的 `chunk_pattern`，语法：`whole`（不切，默认值）／`bytes:<n>`（每 n 字节一片）／`fixed:<n>`（同 bytes，保留别名）／`random:<seed>`（按 seed 生成 1..64 字节的随机片长）／`lines`（按行切，每片一行含行尾）。**节奏（片间延迟）不属于 chunk_pattern**，由用例的 `asserts.perf` 或人工观察处理。缺失时 MUST 按 `whole` 处理 |
| 样本形态（渲染层） | `input.md` + `meta.json`（`layer:"render"`），可选 `append_steps.json`（逐步追加的片段数组，用于流式渲染用例） |
| 样本形态（导入层） | `payload.json`（一份导出文件摘要）+ `meta.json`（`layer:"import"`） |
| Runner 输入 | 样本目录 + 目标实现（android / windows） |
| Runner 输出 | `events.jsonl`（StreamEvent 序列，键排序、无空白）+ `exit.json`（`{ok, case, error?}`） |
| 判据 | (1) 双端 `events.jsonl` 的 SHA-256 相同；(2) 与 `expected.jsonl` 相同（若提供）；(3) `meta.json` 的 `expect_error` 命中 |
| **非确定性字段** | 比对前 MUST 剔除：`failed.error.trace_id`、`failed.error.request_id`、`failed.error.raw` 中由 provider 生成的 id。剔除后仍不相同即为失败。剔除清单的改动需升版本 |
| 非事件断言 | `meta.json` 可选 `asserts`，形状固定：`{db:[{check,args}], socket:[{expect,within_ms}], perf:[{metric,op,value}]}`。**任何一条断言失败 → 该用例失败**（断言是判定的一部分，不是参考资料）；`exit.json.asserts` 必须逐条输出 `{id, ok, actual}`，缺失条目也判失败。缺整个 `asserts` 段仅表示"该用例不检查这些方面"，不得因此放过已有断言。 |
| `db` 断言 | `check` 闭集：①`message_status`，args `{message_id, status}`；②`message_part_truncated`，args `{part, value}`；③`row_count`，args `{table, where, count}`；④`no_rows`，args `{table, where}`。`where` 语法：**扁平对象，列名→期望值，全部 AND**；**空对象 `{}` 表示不过滤（统计全表）**；不支持嵌套与运算符。 |
| 标识符来源（闭集） | `where` 的值与 `args.message_id` 只能是以下三种：①字面量 uuid，②`"last"`（该用例本次运行最后写入的记录，按 `created_at, id` 排序取 1，该表无写入时该断言判失败而不是跳过），③`"$fixture.<name>"`（取 `meta.json.fixtures.<name>`，`meta.json` 缺该键则该用例判失败）。**除此之外任何字面量都算用例缺陷**——CAN-003 曾经写死 `"c0"`、EXPORT-003 曾经断言一个没人写入的字段，都是这么来的 |
| `socket` 断言 | 形状 `{expect:"stream_count_zero", within_ms:<int>}`。唯一判据：从终止事件时间点起，实现暴露的测试钩子 `activeStreamCount()` 在 `within_ms` 内归零。**归零的含义包含"归还到连接池"**——池中空闲连接不算 active（否则与 providers §7 的连接复用冲突） |
| `perf` 断言 | `{metric:"<case-id>", op:"lte"\|"gte", value:<number>}`，单位取 `meta.json.perf_unit`（`ms` / `count` / `bytes`）。**测量方法写死**：重复 N=100 次，丢弃前 10 次预热，取 **p50**，不是单样本——否则忙机器上必然误红。`meta.json` 必须同时给 `perf_method`（如 `"append-10-char"`），与 `metric` 一一对应 |
| 错误路径格式 | 导入类用例的错误路径 MUST 为 JSON Pointer（如 `/data/attachments/3/local_path`），双端同格式 |
| 期望文件 | 每个用例必须有 `expected.jsonl` 或 `expect_error`，二者必居其一 |

## 1. 分帧层（FRAME，13）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| FRAME-001 | 含中文、emoji、非 BMP 字符的流，按 `bytes:1` 逐字节喂入 | 文本完整无乱码；事件序列与 `whole` 切片完全相同 | L1 |
| FRAME-002 | 单个 `data:` 事件被切成 3 段，切点落在 JSON 字符串内部 | 1 个完整事件，无重复无丢失 | L1 |
| FRAME-003 | 每 5 行插入 `: keep-alive` 与 `: OPENROUTER PROCESSING` | 事件序列不含任何心跳相关事件；idle 计时被重置 | L1 |
| FRAME-004 | 移除 `data: [DONE]`，但最后一个 chunk 有 `finish_reason:"stop"` | `done{stop}`（EOF 宽容路径） | L1 |
| FRAME-005 | `[DONE]` 出现在内容中途，其后还有 3 个内容 chunk | `done` 在正确位置；后续字节不产生事件 | L1 |
| FRAME-006 | `[DONE]` 重复 3 次 | 只有一个 `done`，I2 成立 | L1 |
| FRAME-007 | 混合 `\r\n`、`\r`、`\n` 行尾 | 与实际行内容一致，无残留 `\r` 进入 payload | L1 |
| FRAME-008 | 流首 BOM + 中段插入非法 UTF-8（0xFF 0xFE） | BOM 丢弃；非法字节替换为 U+FFFD 并继续；不抛异常 | L1 |
| FRAME-009 | **单行** `data:` 载荷 10 MiB（合法大 JSON，无内部换行） | 正常解析出 1 个事件；无 OOM（Android 堆监控断言）。此例证明单行上限不得低于单帧上限 | L1 |
| FRAME-010 | 单事件载荷 20 MiB（超 16 MiB 上限） | `failed(PARSE_EVENT_TOO_LARGE)`，socket 关闭 | L1 |
| FRAME-011 | provider 发出 `delta.content: ""` 的空增量（3 次），其间夹 2 个正常增量 | 空增量 MUST NOT 产生事件（I6）：`events.jsonl` 只有 2 个 `delta` | L2 |
| FRAME-012 | 正常 `[DONE]` 结束后，服务端继续占用连接不关闭 5 秒 | `done` 后立即满足 `asserts:{"socket":[{"expect":"stream_count_zero","within_ms":100}]}`（I12；归还包括归还连接池） | L1 |
| FRAME-013 | `content_encoding: gzip` 的完整压缩流 | 解压后事件序列与非压缩版本逐字节相同 | L1 |

## 2. OpenAI Chat Completions（OAI，12）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| OAI-001 | 标准 5 个内容 chunk + `[DONE]` | 5 个 `delta` + `done{stop}`，seq 0..5 | L2 |
| OAI-002 | 最后一 chunk 带 `usage`（`stream_options.include_usage`） | `usage` 在 `done` 之前，且仅一个 | L2 |
| OAI-003 | 一个 tool_call 的 `arguments` 被切成 3 片，`name` 只在首片 | 首片带 id+name+args:""，后两片只带 args；`tool_call_end` 1 个；拼接结果等于完整 JSON 字符串 | L2 |
| OAI-004 | 两个 tool_call（index 0/1）交错下发 | 两个 index 各自拼接正确；各一个 `tool_call_end`；`done{tool_calls}` | L2 |
| OAI-005 | 同一 delta 同时含 `content` 与 `reasoning_content` | 两个事件按 seq 顺序（delta 在前）；思考与正文独立 | L2 |
| OAI-006 | `finish_reason:"length"` | `done{length}` | L2 |
| OAI-007 | HTTP 200，body 为 `{"error":{"message":"quota","code":"insufficient_quota"}}` | `failed(AUTH_INSUFFICIENT_QUOTA)`，raw 脱敏后 ≤512 字节 | L2 |
| OAI-008 | `finish_reason:"content_filter"` 且此前有内容 | `done{content_filter}`（不是 failed） | L2 |
| OAI-009 | 同一内容 chunk 字节级重发 2 次，`meta.json` 标 `duplicate: true` | 恰好 1 个 `delta`（`seq` 0），事件总数 = 2（含 `done`）。**仅当** `meta.duplicate=true` 时才去重；真实模型连发两个相同文本时（无此标记）MUST 原样发 2 个 `delta` | L2 |
| OAI-010 | 3 个 chunk 顺序颠倒（模拟代理乱序） | 适配器按到达顺序发事件，seq 仍严格 +1（I1 成立） | L2 |
| OAI-011 | 单个 chunk 同时含 `content` 与两个 `tool_calls` 分片（一帧产 3 事件） | 3 个事件按确定性顺序（`delta` → `tool_call_delta[0]` → `tool_call_delta[1]`），`seq` 连续（I8） | L2 |
| OAI-012 | 前缀累积型端点：同一回复的第 2、3 个 chunk 重发全文 | 只发增量（I11）：最终文本等于最后一次全文，事件数 = 增量数 | L2 |

## 3. OpenAI Responses（RESP，5）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| RESP-001 | `response.created` + 3× `output_text.delta` + `completed` | 3 个 delta + done；`created` 不产生事件 | L2 |
| RESP-002 | `reasoning_summary_text.delta` ×2 与 `output_text.delta` ×2 交错 | reasoning_delta 与 delta 分离，顺序与到达一致 | L2 |
| RESP-003 | `output_item.added(function_call)` + 4× `function_call_arguments.delta` + `done` + `completed` | 1 个 index，args 拼接正确，`tool_call_end` 在 `tool_call_arguments.done` 时发出 | L2 |
| RESP-004 | `completed` 带完整 usage（含 cached、reasoning） | `usage` 字段齐全，位于 `done` 前 | L2 |
| RESP-005 | `response.failed`（`server_error`） | `failed(SERVER_ERROR)` | L2 |

## 4. Anthropic（ANT，9）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| ANT-001 | `message_start`+2×text_delta+`message_delta`+`message_stop` | 2 delta + usage（input 来自 start，output 来自 delta）+ done{stop} | L2 |
| ANT-002 | thinking_delta ×3 与 text_delta ×2 交错 | 两种事件分离，顺序正确 | L2 |
| ANT-003 | tool_use block：`content_block_start`+`input_json_delta`×3+`content_block_stop` | 1 index，args 拼接正确，`tool_call_end` 在 block_stop | L2 |
| ANT-004 | 每 2 个事件插入 `ping` | 无事件产生 | L2 |
| ANT-005 | 中途 `error{type:overloaded_error}`，此前已有 2 个 text_delta | 先保留 2 个 delta，再 `failed(SERVER_OVERLOADED)`；落盘标记 `truncated=true` | L2 |
| ANT-006 | `event:` 行缺失（只有 data），以及 `event:` 与 `data:` 顺序颠倒 | 仍能正确分派（按 `data.type` 兜底）；不产生错位事件 | L2 |
| ANT-007 | `message_delta.usage` 只含 `output_tokens`，input 只在 `message_start` | usage 合并为一条，input/output 均正确 | L2 |
| ANT-008 | `stop_reason:"max_tokens"` | `done{length}` | L2 |
| ANT-009 | 块顺序：0=thinking、1=text、2=tool_use(id=A)、3=text、4=tool_use(id=B) | `eventIndex` 为 0（A）与 1（B），**不是** provider 的 block index 2 与 4；两个 `tool_call_end` 分别对应 0/1 | L2 |

## 5. Gemini（GEM，6）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| GEM-001 | `alt=sse` 模式，3 个 chunk，末 chunk 带 usageMetadata | 3 delta + usage + done | L2 |
| GEM-002 | 数组模式（`[{...},{...}]`）且数组被切成 17 字节片段 | 每个对象恰好产生对应事件，无粘连/截断 | L2 |
| GEM-003 | 累积文本模式（每次发送全文，共 4 次） | 只发出增量部分（去重生效），最终文本与全文一致 | L2 |
| GEM-004 | `functionCall` 一次性下发完整 args | 1 个 `tool_call_delta`（带完整 args）+ 1 个 `tool_call_end` | L2 |
| GEM-005 | 仅 `promptFeedback.blockReason:"SAFETY"`，无 candidates | `failed(CONTENT_FILTERED)`，无 delta | L2 |
| GEM-006 | `finishReason:"SAFETY"` 且此前已有 2 个 delta | 2 delta + `done{content_filter}` | L2 |

## 6. OpenRouter（ORT，4）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| ORT-001 | `: OPENROUTER PROCESSING` 与内容交错 | 无事件；idle 重置 | L2 |
| ORT-002 | 末 chunk `usage:{prompt_tokens,completion_tokens,cost:0.00231}` | `usage.provider_cost={amount:"0.00231",currency:"USD"}` | L2 |
| ORT-003 | `data: {"error":{"code":502,"message":"Provider returned error"}}` | `failed(SERVER_ERROR)` | L2 |
| ORT-004 | 429 + `Retry-After: 20` + `x-ratelimit-remaining-requests: 0` | `failed(RATE_LIMITED, retry_after_ms:20000)`；头被记录到 trace，不进入 StreamEvent | L2 |

## 7. 错误映射（ERR，6）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| ERR-001 | HTTP 401，body `{"error":{"code":"invalid_api_key"}}` | `failed(AUTH_INVALID_KEY)`，`retryable=false`，action 含 `edit_provider` | L2 |
| ERR-002 | HTTP 429，body `insufficient_quota` | `failed(AUTH_INSUFFICIENT_QUOTA)`，action 含 `top_up` | L2 |
| ERR-003 | HTTP 400，`context_length_exceeded` | `failed(CONTEXT_OVERFLOW)`，action 含 `reduce_context` | L2 |
| ERR-004 | HTTP 404，message 含 model | `failed(MODEL_NOT_FOUND)` | L2 |
| ERR-005 | 传输中途 TCP RST（样本用 meta 标记 `abort_at_byte`） | `failed(NET_CONNECTION_RESET)`；已生成内容保留 | L1 |
| ERR-006 | 长时间无字节（meta `idle_ms=181000` 加速为 500ms 由测试配置覆盖） | `failed(NET_READ_TIMEOUT)` | L1 |

## 8. 取消与并发（CAN，4）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| CAN-001 | 连接建立后、首字节前取消 | 恰好一个 `done{aborted_user}`，无 `failed`；`asserts:{"socket":[{"expect":"stream_count_zero","within_ms":200}],"db":[{"check":"message_status","args":{"message_id":"last","status":"aborted"}}]}` | L3 |
| CAN-002 | 收到 3 个 delta 后取消 | 3 delta + `done{aborted_user}`；DB 中该 message `status='aborted'`，最后一个 part `truncated=1`（ADR-003 §2.3：用户取消一律置 1，不论是否已有 delta）；`asserts:{"db":[{"check":"message_part_truncated","args":{"part":"last","value":true}}],"socket":[{"expect":"stream_count_zero","within_ms":200}]}` | L3 |
| CAN-003 | 取消后 10ms 内发起新请求 | 旧流进入 Terminal 后才发出新请求，无交错写同一 message；`asserts:{"db":[{"check":"row_count","args":{"table":"message","where":{"conversation_id":"$fixture.conversation","status":"streaming"},"count":1}}]}`（同一会话同时最多 1 条 streaming；`meta.json.fixtures.conversation` 必须在用例前建好） | L3 |
| CAN-004 | 取消后检查连接池 | `asserts:{"socket":[{"expect":"stream_count_zero","within_ms":200}]}`；另记 trace 断言：OkHttp `EventListener.callEnd` 与 SocketsHttpHandler 连接计数各触发一次 | L3 |

## 9. 渲染模型（RENDER，15）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| RENDER-001 | 500 行代码块，流式逐 10 行喂入 | 始终为 `code{closed:false}`，闭合后 `closed:true`；无异常 | L1 |
| RENDER-002 | 4 层嵌套列表（含有序/无序混合） | blocks 结构与 `model.json` 一致 | L1 |
| RENDER-003 | 表格含对齐、转义竖线 `\\|`、行内代码 | 列数、对齐、内容正确 | L1 |
| RENDER-004 | 行内 `$x^2$` + 块级 `$$...$$` | `math` inline/block 各就位 | L1 |
| RENDER-005 | 30 节点 flowchart TD | `mermaid{kind:"flowchart"}`，`closed:true` | L1 |
| RENDER-006 | sequenceDiagram 含 alt 块 | `mermaid{kind:"sequence"}` | L1 |
| RENDER-007 | 中英混排 + emoji（含非 BMP）+ 全角标点 | 文本逐字符一致（UTF-16 代理对不得拆断） | L1 |
| RENDER-008 | RTL（阿拉伯文）与 LTR 混排 | 文本顺序不反转；由渲染层做双向算法 | L1 |
| RENDER-009 | 脚注定义 + 引用 | `footnote_def` + `footnote_ref` 配对 | L1 |
| RENDER-010 | 任务列表 `- [x]`/`- [ ]` | `list.items[].checked` 正确 | L1 |
| RENDER-011 | 原始 HTML（`<script>alert(1)</script>`） | `html` 块，`source` 原样，**不执行**；渲染为字面文本 | L1 |
| RENDER-012 | 10 万字符 + 末尾追加 10 字符（重复 100 次） | 前缀稳定性（§4.5）成立；`asserts:{"perf":[{"metric":"append-10","op":"lte","value":2}]}`，`meta.perf_unit="ms"` | L1 |
| RENDER-013 | 未闭合代码块内嵌表格语法（对抗） | 表格语法被当作代码文本，不被解析为 table | L1 |
| RENDER-014 | 段落只含一个 `![a](x.png)`；另一段落为 `见 ![b](y.png) 图` | 前者产 Block `image`；后者产 `paragraph{inlines:[text,image,text]}`（§2.2 判定） | L1 |
| RENDER-015 | `看这个 [文档][d]` + 后到的 `[d]: https://x` | v1 不解析引用式链接：前者 `text` 原样含方括号，后者为 `unknown{kind:"link_definition"}`；且追加定义后**前面已完成块不变**（§4.5） | L1 |

## 10. 导出导入（EXPORT，4）

| ID | 场景与构造 | 期望 | 层 |
| --- | --- | --- | --- |
| EXPORT-001 | 1000 组随机会话（生成器 seed 固定）往返 | 两次导出 `data` 逐字段相同（§notes.2） | L3 |
| EXPORT-002 | 含分支树、6 条附件引用、用量记录、思考内容 | 同上；附件元数据与引用关系完整 | L3 |
| EXPORT-003 | 恶意文件：40 层嵌套、`local_path:"../../etc/passwd"`、单字段 30MB | 三种都被拒绝并给出 JSON Pointer 路径；`asserts:{"db":[{"check":"row_count","args":{"table":"conversation","where":{},"count":0}},{"check":"row_count","args":{"table":"message","where":{},"count":0}}]}`（用例在空库上跑；空 `where` = 全表，计数 0 证明整体回滚无残留） | L3 |
| EXPORT-004 | `format_version: 2` | `IMPORT_UNSUPPORTED_VERSION`，不尝试部分导入 | L3 |

## 10.5 不变量覆盖表（I1..I12 → 用例）

| 不变量 | 覆盖用例 |
| --- | --- |
| I1 | OAI-001、OAI-010 |
| I2 | FRAME-005、FRAME-006、OAI-001 |
| I3 | ANT-005、OAI-007 |
| I4 | OAI-002、ANT-001、RESP-004 |
| I5 | OAI-003、OAI-004、ANT-003 |
| I6 | FRAME-011 |
| I7 | OAI-003、ANT-003、GEM-004 |
| I8 | OAI-011、ANT-001（usage 合并） |
| I9 | FRAME-003、ANT-004、ORT-001 |
| I10 | CAN-001、CAN-002 |
| I11 | OAI-012、GEM-003 |
| I12 | FRAME-012、CAN-004 |

## 11. 覆盖矩阵与 Gate 归属

| Gate | 必须全绿的用例组 |
| --- | --- |
| G0 | runner 本身可运行；FRAME / ERR / CAN 的样本全部入库；78 条用例的 `meta.json` 与 `expected.jsonl`/`expect_error` 齐备 |
| G1 | FRAME、OAI、ERR、CAN 全部；双端 SHA-256 相同 |
| G2 | 追加 RESP、ANT、GEM、ORT 全部 |
| G3 | RENDER 全部 + EXPORT 全部 |
| G4 | 追加 TOOL/MCP 用例（M4 落盘，届时补 ID） |
