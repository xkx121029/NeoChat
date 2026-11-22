# spec/stream-events.md — 流式事件契约

- 版本：**1.0.1**
- 状态：**FROZEN**（2026-11-22；四轮独立评审关闭，见 docs/reviews/spec-review-1~4.md）
- 起草日期：2026-11-22
- 适用：Android 与 Windows 的传输层、协议层、状态层；conformance runner
- 规范性用语：MUST / MUST NOT / SHOULD / MAY 按 RFC 2119 解释

本文件是流式行为**唯一的事实来源**。实现与本文件冲突，以实现为缺陷；本文件有歧义，由 Tech Lead 改写并升版本，执行者不得自行裁决。

---

## 1. 两层模型

```
字节流 (HTTP body, 可能 gzip) 
   └─[L1 分帧层]→ SSE 帧 (event/data/comment/blank)
        └─[L2 归一化层]→ StreamEvent (JSON Lines)
             └─[L3 消费层]→ UI 状态 / 持久化
```

L1 与 L2 的实现是双端各自一份，但 L2 的**输出必须逐字节相同**（同一输入字节流 → 同一 StreamEvent 序列），这是 M1/G1 的核心判据。

---

## 2. L1 分帧层（SSE）

### 2.1 字节切分规则（MUST）

1. MUST NOT 假设 chunk == 行 == 事件。一次 TCP 读取可能包含 0.3 个事件，也可能包含 40 个事件。
2. MUST 用**有状态的增量 UTF-8 解码器**（Android：`CharsetDecoder` + `CoderResult.UNDERFLOW` 保留残余字节；Windows：`Decoder`/`Encoding.UTF8.GetDecoder()` + `Decoder.Convert`）跨 chunk 保留不完整的多字节序列。**禁止** 对每个 chunk 单独 `bytes.toString(UTF_8)`。
3. 行结束符 MUST 同时接受 `\n`、`\r\n`、`\r`（后者极少但存在）。
4. 空行（仅行结束符）为**事件分隔符**，MUST 派发当前累积的帧。
5. 以 `:` 开头的行是**注释（心跳）**，MUST 丢弃，MUST 只用于重置读空闲计时。
6. 字段解析：`field: value`，冒号后**一个**前导空格 MUST 被剥离；同一事件内多个 `data:` 行 MUST 用 `\n` 连接后作为载荷。
7. `event:`、`id:`、`retry:` 字段 MUST 被解析并保留在帧对象中（Anthropic 用 `event:` 分派）。
8. UTF-8 起始处的 BOM（`EF BB BF`）MUST 被丢弃。
9. 非法 UTF-8 字节 MUST 替换为 U+FFFD 并继续解析（不抛异常），在 trace 中计数 `invalid_utf8_bytes`。
10. 单帧载荷上限：**16 MiB**（可配置，下限 1 MiB）。超限 → `failed(PARSE_EVENT_TOO_LARGE)`，并 MUST 立即关闭 socket 防止内存耗尽。
11. **单行上限与单帧上限相同**（16 MiB），对 `data:` 行与其它行一视同仁。禁止为单行设更严的上限：一次 10 MiB 的合法单行 `data:` 必须被接受（FRAME-009 即此形态）。

### 2.2 空闲与超时

| 计时器 | 默认 | 语义 |
| --- | --- | --- |
| connect | 10s | TCP+TLS 建连 |
| first_byte | 60s | 请求发出到响应头/首字节（长思考模型需要更长：provider 级可配，上限 300s） |
| read_idle | 180s | 两次**任何**传输字节（含心跳注释）之间的间隔。下限 30s |
| total | 无默认上限 | provider 级可配 |

read_idle 触发 → `failed(NET_READ_TIMEOUT)`。注意：注释行 MUST 重置 read_idle，否则 Anthropic 的 `ping` 会被误判超时。

### 2.3 结束语义

- 正常结束：收到 provider 的终止标记（OpenAI `[DONE]`。**终止标记存在即 `done`**：`[DONE]` 已出现但整条流从未给出 `finish_reason` 时，`finish_reason` 取 `stop`，这不属于 §2.3 的 `PARSE_INCOMPLETE`——那条只适用于**既无终止标记也无 `finish_reason`** 的情况；；Anthropic `message_stop`；Responses `response.completed` 或 `response.incomplete`（后者表示被 `max_output_tokens` 截断，仍按 `done{length}` 结束）；Gemini 数组结束或 `alt=sse` 流 EOF）→ 必须已发出且仅发出一个终止事件。
- EOF 但无终止标记：
  - 若已收到 `finish_reason`/`stop_reason`/`finishReason` → 补发 `done`（宽容路径，常见于兼容端点省略 `[DONE]`）。
  - 否则 → `failed(PARSE_INCOMPLETE)`，携带 `partial_text_length`。
- HTTP 状态非 2xx：MUST 读完（或限 64 KiB 截断）响应体用于错误映射，然后 `failed`，MUST NOT 尝试解析为 SSE。
- HTTP 200 但响应体是 JSON 错误对象（Content-Type `application/json` 且首字符为 `{`）：MUST 按错误对象映射到 `failed`，错误码 `PARSE_SSE_INVALID` 或更具体的 provider 映射（见 errors.md §5）。

### 2.4 压缩

`Content-Encoding: gzip/deflate` MUST 由传输层透明解压。压缩流截断 → 读错误 → `NET_CONNECTION_RESET`。MUST NOT 出现在 L1 逻辑里。

---

## 3. L2 事件模型

### 3.1 线格式

JSON Lines，UTF-8，**一行一个事件**，无空行、无尾随逗号。字段名短小写。`seq` 从 0 单调递增，每流独立，重复即违约。

**键序不参与判据**：实现可任意排列事件内的键，比对前由 runner 统一按字典序规范化（`CASES.md` §0）。因此"键序不同"不是缺陷，`events.jsonl` 的 sha256 也不受它影响。

```jsonl
{"t":"delta","seq":0,"text":"你"}
{"t":"reasoning_delta","seq":1,"text":"先看用户意图"}
{"t":"tool_call_delta","seq":2,"index":0,"id":"call_a1","name":"web_search","args":"{\"q\":"}
{"t":"tool_call_delta","seq":3,"index":0,"args":"\"天气\"}"}
{"t":"tool_call_end","seq":4,"index":0}
{"t":"usage","seq":5,"input":412,"output":37,"cached":0,"reasoning":12,"total":449,"estimated":false}
{"t":"done","seq":6,"finish_reason":"tool_calls"}
```

### 3.2 事件类型表

| `t` | 必填字段 | 可选字段 | 语义 |
| --- | --- | --- | --- |
| `delta` | `text`(非空) | — | 正文增量。消费者按 `seq` 顺序**追加**即得全文 |
| `reasoning_delta` | `text`(非空) | — | 思考/推理增量，**独立通道**，禁止与正文拼接 |
| `tool_call_delta` | `index`(≥0) | `id`, `name`, `args` | 工具调用增量。同一 `index` 的 `args` 按 `seq` 顺序拼接即得完整 JSON |
| `tool_call_end` | `index` | — | 该 index 不再有增量。每个 index MUST 恰好一个 |
| `usage` | `input`,`output` | `cached`,`reasoning`,`total`,`estimated`,`error_bound_pct`,`provider_cost` | **累计最终值**，每流最多一个，且 MUST 位于终止事件之前 |
| `notice` | `code` | `params` | 非致命提示（能力剥离、输入截断、多 choice 忽略、usage 为估算等） |
| `done` | `finish_reason` | `raw_finish_reason` | 正常终止 |
| `failed` | `error` | — | 异常终止。`error` 为 errors.md §2 的完整对象 |

`finish_reason` 枚举（闭集，不得扩展）：`stop` | `length` | `tool_calls` | `content_filter` | `aborted_user`。

`notice.code` 枚举（闭集）：`capability_stripped` | `input_truncated` | `multi_choice_ignored` | `usage_estimated` | `reasoning_unsupported` | `provider_downgraded` | `stream_interrupted`。`params` 为 `map<string, string | string[]>`（如 `{"dropped":["vision","tools"]}`），数组值就是规范，不得再序列化成逗号串。

`usage.provider_cost`：`{"amount":"0.00231","currency":"USD"}`，仅当 provider 明确回报费用（OpenRouter）时出现；有它时以它为准，本地估算仍记录在 `usage_record.cost_estimate` 供对比。

### 3.3 不变量（可判定，conformance 逐条断言）

| ID | 不变量 |
| --- | --- |
| I1 | `seq` 从 0 开始、严格 +1、无重复无空洞 |
| I2 | 最后一个事件必为 `done` 或 `failed`，且只出现一次 |
| I3 | `failed` 之后不得有任何事件 |
| I4 | `usage` 至多一个，且若存在必在终止事件之前 |
| I5 | 所有 `tool_call_end` 必在 `done` 之前；每个打开过的 index 恰好一个 `tool_call_end`；`done{finish_reason:"tool_calls"}` 意味着至少一个 index 曾打开 |
| I6 | `delta.text` 与 `reasoning_delta.text` 均非空字符串（空串 MUST NOT 发射） |
| I7 | 每个 index 的**首个** `tool_call_delta` MUST 携带 `id` 与 `name`（`id` 缺失时适配器必须合成 `call_<index>`）；若该首片同时带了 `args`，MUST 原样发送，**不得因为"首片只放 id/name"的误读而丢弃参数**；后续增量 MUST NOT 重复携带 `name` |
| I8 | 事件流与网络帧解耦：一个 provider 帧可产生 0..N 个事件；一个事件 MUST NOT 跨两个 provider 帧被拆分（表现为半条 JSON）。**唯一例外**：`usage` 允许由多帧合并而成（Anthropic 的 input 在 `message_start`、output 在 `message_delta`），合并后仍只发一条 |
| I9 | 心跳/注释/空帧 MUST NOT 产生任何事件 |
| I10 | 用户取消 MUST 以 `done{finish_reason:"aborted_user"}` 结束，MUST NOT 为 `failed` |
| I11 | **仅对累积型端点**（Gemini 数组模式、被标记 `cumulative_stream=true` 的兼容端点）：一次尝试内 `delta` MUST NOT 重发已发过的前缀，适配器须维护 `emitted_prefix_len` 做差集。增量型端点（OpenAI/Anthropic）不适用，去重条件见 §4.1 |
| I12 | 终止事件发出后 MUST 关闭 socket 或归还连接到池（不得让连接悬挂到空闲超时） |

### 3.4 消费层义务

- UI 的"全文"= 按 `seq` 连接的全部 `delta.text`。禁止在适配器外做二次拼接或去重。
- 思考内容独立展示（可折叠），默认不写入导出正文，但 MUST 持久化（`message_part.kind='reasoning'`）。
- `usage` 缺失时，消费者 MAY 用估算值，但 MUST 以 `estimated=true` + `error_bound_pct` 呈现，禁止伪装为精确值。
- `notice` 必须可达用户（不静默）：能力剥离类必须给出"剥离了什么"的可读文案。

---

## 4. Provider 映射

### 4.1 OpenAI Chat Completions（含所有 `openai_compatible`）

请求用 `stream: true`，`stream_options: {"include_usage": true}`（端点不支持时降级并 `notice(usage_estimated)`）。

| provider 帧 | 映射 |
| --- | --- |
| `data: {choices:[{delta:{content:"..."}}]}` | `delta` |
| `delta.reasoning_content`（DeepSeek/Kimi/硅基流动）或 `delta.reasoning`（若干兼容实现） | `reasoning_delta`。两者同帧出现时 `reasoning_content` 优先，另一个丢弃并在 trace 记 `reasoning_field_conflict` |
| `delta.tool_calls[i]` | 首个分片携带 `id`/`function.name` → `tool_call_delta{index:i,id,name,args:""}`；后续只带 `function.arguments` 分片 → `tool_call_delta{index:i,args:"..."}` |
| `choices[0].finish_reason` 非 null | 记录待终止；不立即 `done`（usage 可能随后到） |
| `usage:{prompt_tokens,completion_tokens,total_tokens,prompt_tokens_details.cached_tokens}` | `usage`（合并为唯一一条） |
| `data: [DONE]` | 为每个仍打开的 index 发 `tool_call_end`，然后 `done` |
| `choices.length > 1` | 只用 `index==0`，其余丢弃 + `notice(multi_choice_ignored)` |
| `data: {"error":{...}}` | `failed`（errors.md §5.1） |

**一帧多事件时的固定顺序**：先发 `delta`，再按 `tool_calls[].index` 升序发 `tool_call_delta`（同 index 内按 `function.arguments` 顺序）。

**重复 chunk 的去重判据**：OpenAI 系端点是**增量**语义，`delta.content` 相同不代表重复（模型可以合法地连发两个相同 token）。因此：

- 仅当 conformance 的 `meta.duplicate=true`，或该 provider 的 `provider.cumulative_stream=1` 时（真实列，见 `v1.sql`；不是 `extra_json` 里的私货），适配器才 MUST 用 `emitted_prefix_len` 做前缀差集；
- 其余情况 MUST 原样发射，**禁止**按内容去重（会吞掉真实内容）。
- 累积端点若新文本**不以已发前缀开头**（说明是重写而非追加），MUST 丢弃该帧并记 trace `cumulative_rewrite_ignored`，不得重置已发内容、不得回退 UI（Gemini 同规则见 §4.4）。

`finish_reason` 映射（只对**出现过**的取值映射）：`stop`→`stop`；`length`→`length`；`tool_calls`/`function_call`→`tool_calls`；`content_filter`→`content_filter`。**字段缺失时不得映射为 `stop`**：EOF 一律回到 §2.3 判定（从未见过 `finish_reason` → `failed(PARSE_INCOMPLETE)`）。

### 4.2 OpenAI Responses API

| provider 事件 | 映射 |
| --- | --- |
| `response.created` | 无事件（可记 trace） |
| `response.output_text.delta` `{delta}` | `delta` |
| `response.reasoning_summary_text.delta` 或 `response.reasoning_text.delta` | `reasoning_delta` |
| `response.output_item.added` `{item:{type:"function_call",id,name,call_id}}` | 分配 `index`（按出现顺序），`tool_call_delta{index,id:call_id,name,args:""}` |
| `response.function_call_arguments.delta` `{item_id,delta}` | `tool_call_delta{index,args:delta}`（按 item_id → index 映射） |
| `response.function_call_arguments.done` | `tool_call_end{index}` |
| `response.completed` `{response:{usage:{input_tokens,output_tokens,input_tokens_details.cached_tokens,output_tokens_details.reasoning_tokens}}}` | `usage` 然后 `done`（`stop_reason` 由 `response.incomplete_details.reason` 决定：`max_output_tokens`→`length`，否则 `stop`） |
| `response.failed` / `error` | `failed` |
| `response.in_progress` 等其他事件 | 忽略 |

### 4.3 Anthropic Messages

请求 `stream: true`。`event:` 字段是**首选**分派依据；`event:` 缺失时 MUST 按 `data.type` 分派；两者同时存在且冲突时以 `data.type` 为准并记 trace（对应 ANT-006）。

| provider 事件 | 映射 |
| --- | --- |
| `message_start` | 记 `usage.input_tokens`、`cache_read_input_tokens`、`cache_creation_input_tokens`（供最终 usage 合并） |
| `content_block_start` `{index,content_block:{type:"text"}}` | 无事件 |
| `content_block_start` `{type:"thinking"}` | 无事件（后续 thinking_delta 承载） |
| `content_block_start` `{index,content_block:{type:"tool_use",id,name}}` | `eventIndex` = 本流中 `tool_use` 块的**出现顺序**（从 0 起，text/thinking 块不占号），`tool_call_delta{index:eventIndex,id,name,args:""}` |
| `content_block_delta` `{delta:{type:"text_delta",text}}` | `delta` |
| `content_block_delta` `{delta:{type:"thinking_delta",thinking}}` | `reasoning_delta` |
| `content_block_delta` `{delta:{type:"input_json_delta",partial_json}}` | `tool_call_delta{index:eventIndex,args:partial_json}`（按该 delta 所属 content block 的 provider `index` 反查 `eventIndex`） |
| `content_block_delta` `{delta:{type:"signature_delta"}}` | 忽略（记 trace） |
| `content_block_stop` （该 block 为 tool_use） | `tool_call_end{index:eventIndex}` |
| 未列出的 `content_block_start` 类型（`redacted_thinking`、`citations_delta`、`server_tool_use` 等） | MUST 忽略并记 trace，MUST NOT 当作正文或思考上屏 |
| `message_delta` `{delta:{stop_reason},usage:{output_tokens}}` | 记 stop_reason 与 output_tokens |
| `message_stop` | `usage`（合并 `message_start` 的 input + `message_delta` 的 output）→ `done` |
| `ping` | 无事件（仅重置 idle） |
| `error` `{type,message}` | `failed`（errors.md §5.3） |

`stop_reason` 映射：`end_turn`→`stop`；`stop_sequence`→`stop`；`max_tokens`→`length`；`tool_use`→`tool_calls`；`refusal`→`content_filter`。

注意：Anthropic 的 `error` 事件在流中途出现时，MUST 先落盘已生成内容再 `failed`。

### 4.4 Google Gemini generateContent

请求必须用 `streamGenerateContent?alt=sse`。若端点拒绝 `alt=sse`（返回 JSON 数组），MUST 回退到数组模式：按**顶层 JSON 值的边界**流式切出对象——跟踪 `{}`/`[]` 深度与字符串/转义状态，深度归零即一个完整对象（必须用增量解析，不得累积全量后再解析）。禁止用正则按逗号切分：字符串内部的逗号与转义序列会导致切错。

| provider 帧 | 映射 |
| --- | --- |
| `candidates[0].content.parts[i].text`，且 `parts[i].thought != true` | `delta`（见下方"累积去重"规则） |
| `candidates[0].content.parts[i].text` 且 `thought == true` | `reasoning_delta` |
| `candidates[0].content.parts[i].functionCall:{name,args}` | `tool_call_delta{index:顺序号,id:"call_<n>",name,args:JSON.stringify(args)}` 然后立即 `tool_call_end`（Gemini 不流式输出参数） |
| `candidates[0].finishReason` | 记录：`STOP`→`stop`；`MAX_TOKENS`→`length`；`SAFETY`/`RECITATION`/`BLOCKLIST`/`PROHIBITED_CONTENT`→`content_filter`；`OTHER`→`stop` |
| `usageMetadata` | `usage{promptTokenCount, candidatesTokenCount, cachedContentTokenCount, thoughtsTokenCount}` |
| `promptFeedback.blockReason` 且无 candidates | `failed(CONTENT_FILTERED)` |

**累积去重规则（MUST）**：部分 Gemini 版本/代理会发送**累积**文本（每次带全文）。适配器 MUST 维护 `emitted_len`，只发射 `text[emitted_len:]`；若新 text 不以已发前缀开头（说明不是累积而是重写）→ 丢弃该帧并 trace `gemini_rewrite_ignored`。

### 4.5 OpenRouter

= OpenAI Chat Completions 映射 + 以下差异：

- 注释帧 `: OPENROUTER PROCESSING` MUST 被丢弃并重置 idle。
- `data: {"error":{"code":..,"message":..},"id":..}` → `failed`。
- 最终 `usage` 含 `cost`（美元浮点）→ `usage.provider_cost`。
- `X-RateLimit-*` 响应头 MUST 记录（供 M2 多 Key 冷却使用），但 MUST NOT 进入 StreamEvent。

---

## 5. 取消、重试、并发

按 ADR-003 执行。本文件只规定与事件流相关的部分：

1. 取消：适配器 MUST 取消底层 socket，然后在已发事件之后补 `done{finish_reason:"aborted_user"}`，即使尚无 delta。
2. 重试：仅在**尚未发射任何事件**时允许自动重试；重试发出的是同一份请求字节快照。
3. 并发：同一 message 同一时刻只允许一个活跃流（ADR-003 §2.2）。
4. 终止后：MUST 立即释放连接，不得悬挂。
5. 悬挂流修复：进程被杀会留下未终止的流，启动时 MUST 把超过 60s 未更新的 `status='streaming'` 消息改为 `aborted`（`finish_reason` 置 null），保留已落盘内容，并追加一条 `notice` part（`code=stream_interrupted`）说明中断；MUST NOT 自动重发。

---

## 6. Conformance 义务

本文件的每条不变量都对应 `spec/conformance/CASES.md` 中的用例。适配器 PR 必须附：用例 ID → 通过/失败 的原文输出。`I1..I12` 任一失败即返工。

双端 conformance runner 的输入是**原始字节文件**（`.sse`/`.bin`），输出是 JSON Lines 事件流。判据：两端输出文件的 SHA-256 相同。

---

## 7. v1 明确不做

| 项 | 原因 |
| --- | --- |
| 图像/音频流式输出 | 各家尚未稳定，且渲染模型未覆盖 |
| 流式结构化输出（partial JSON 校验） | 各家语义不一（OpenAI 无、Gemini 有），v1 只在结束时整体校验 |
| 多 choice 输出 | 无用户需求证据；仅丢弃并 notice |
| 断点续传 | 无可用 provider 接口（ADR-003 §3）。回前台的“继续生成”是**客户端续写**（新消息），不是续传 |
| 客户端侧内容安全过滤 | 只做 provider 返回的过滤映射，不自建审查 |

## 8. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 首版 |
