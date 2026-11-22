# NeoChat 契约独立评审（本地）

- 评审对象：`spec/stream-events.md` 1.0.0-draft.2、`spec/errors.md` 1.0.0-draft、`spec/providers.md` 1.0.0-draft、`spec/render-model.md` 1.0.0-draft.2、`spec/schema/v1.sql`、`spec/schema/export-v1.json` + `export-v1.notes.md`、`spec/conformance/CASES.md`
- 交叉参照：ADR-001 / ADR-002 / ADR-003、`RULINGS-2026-11-22.md`、`CONTRIBUTING.md`
- 评审者：独立评审（非作者），本机执行，未进云端
- 评审日期：2026-11-22
- 唯一标准：**一个执行者只读这份文件，能不能写出唯一正确的实现。**

## 0. 只读证据

评审前后文件未变动，`git status --short` 无输出（工作树干净，未修改任何一个字节）：

```
920e5034b22cc900a67da7302fb2d2a9dfb2140d3feaadf6129c6e8d45a7a1ec *spec/stream-events.md
3d9906277b74dd6928842536667b8b81607cb100eb88f8dc435784e21b0246ee *spec/errors.md
e75e6ad7ac5c7694118f74c840c38d5f5a982a94d9c1a53e5df913e3fad4bab7 *spec/providers.md
80364cb5058d2ff204d268853306cdc6eee72a3da52036d797ed0566ea62ff25 *spec/render-model.md
75593899a3f94eb971ae750fbfdeedb1e28200ec2c95779f23a1175ccdf1387d *spec/schema/v1.sql
11ae23cc2b558ca31f3bf7274b108f83a5962038edf3e8bb0b6145f67a8b2978 *spec/schema/export-v1.json
43b7135c25d262714219d2a190e7bdd721d0b992e96d3146ac8f05f243587925 *spec/schema/export-v1.notes.md
5cf494ed998d232bdfee7d17404b72f2c75959c35de312d94cf6d2da19fd1c48 *spec/conformance/CASES.md
```

## 1. 总裁决

七份文件**没有一个需要重写**，但没有一份可以直接冻结。共 **29 条 MUST-FIX、19 条 SHOULD-FIX**。

最先该修的三条（其余缺陷都会放大它们）：

1. `errors.md` §2 的 `trace_id`（运行时随机 UUIDv7，必填）与 `CASES.md` §0 的"双端 `events.jsonl` SHA-256 相同"在数学上互斥。**只要有一条 `failed` 事件，G1/G2 永远红**，而按 spec 删掉 `trace_id` 又是违约。这一条不修，conformance 从第一天就不可能绿。表现为 ERR-001~006、OAI-007、ORT-003、RESP-005、ANT-005 全部无法验收。
2. `stream-events.md` §2.3 与 §4.1 对"EOF 且无终止标记、也无 `finish_reason`"给出了相反结论（`failed(PARSE_INCOMPLETE)` vs `done{stop}`）。这是流式契约的核心分支，两端各写各的，M1 的逐字节判据直接失效。
3. `stream-events.md` §2.1 规则 11 的 8 MiB 单行上限与 `CASES.md` FRAME-009 的 10 MiB 必过用例互斥。同一份 10 MiB 单行载荷，一个实现必须报 `PARSE_EVENT_TOO_LARGE`，另一个必须通过。

每条缺陷的正文格式：位置 → 原文 → 两种合理解读 → 一句话改法。`【字节】` 表示修掉之前两端输出字节必然不同；`【行为】` 表示用户可见行为不同；`【判据】` 表示 spec 自身或与用例/ADR 冲突，导致 conformance 无法定义通过条件；`【实现】` 表示照做不可实现（缺字段、缺来源）。

---

## 2. spec/stream-events.md（1.0.0-draft.2）

### MUST-FIX

**MF-S1 §2.1 规则 11（L40）与 CASES FRAME-009 互斥**【判据】
> 原文：L39-40「单帧载荷上限：**16 MiB**（可配置，下限 1 MiB）。超限 → `failed(PARSE_EVENT_TOO_LARGE)`」／L40「11. 单行上限：8 MiB。超限同 10。」
> CASES L34：`FRAME-009 | 单事件载荷 10 MiB（合法大 JSON） | 正常解析，无 OOM`。
> 解读 A：10 MiB 载荷是合法的（≤16 MiB），必须正常解析 → 通过 FRAME-009。解读 B：SSE 的 `data:` 通常只有一行，10 MiB 单行 > 8 MiB 单行上限 → 必须 `failed(PARSE_EVENT_TOO_LARGE)` → FRAME-009 必挂。
> 改法：删掉 8 MiB 单行上限，或把它抬到 ≥16 MiB；保留两种上限就必须在 §2.1 明写"单行上限只约束非 `data:` 行"，并让 FRAME-009 的样本形态（单行还是多行）写死在用例里。

**MF-S2 §2.3（L57-58）与 §4.1（L146）对同一输入给出相反终止**【字节】
> 原文：L57-58「若已收到 `finish_reason`…→ 补发 `done`…否则 → `failed(PARSE_INCOMPLETE)`」；L146「`finish_reason` 映射：…；缺失→`stop`（EOF 宽容路径）。」
> 解读 A：EOF 无终止标记且从未见过 `finish_reason` → `failed(PARSE_INCOMPLETE)`（§2.3 是专门章节）。解读 B：`finish_reason` 缺失即映射为 `stop`，补发 `done{stop}`（§4.1 是 provider 映射表，执行者会以为这是缺字段的兜底）。
> 改法：删掉 §4.1 中"缺失→`stop`"这一项，改为"`finish_reason` 缺失时不得映射，按 §2.3 判定"，并在 §4.1 表下加一行指引。

**MF-S3 §4.3（L171）Anthropic 的 `eventIndex` 映射规则不可判定**【字节】
> 原文：L171「`content_block_start` `{type:"tool_use",id,name}` | 分配 `eventIndex`（与 provider 的 `index` 不必相同，MUST 映射），`tool_call_delta{index,id,name,args:""}`」；同表 L174 只写「`tool_call_delta{index,args:partial_json}`」，L176 只写「`tool_call_end{index}`」。
> 解读 A：按字面"不必相同"，`index` 是重排后的工具序号（0,1,2…），delta 与 stop 都发 `eventIndex`。解读 B：`index` 直接用 provider 的 content block index（文本/thinking 块占位时从 1 起）。同一段 SSE 两端产出 `tool_call_delta.index` 不同 → `events.jsonl` SHA 不同；且 CASES ANT-003 只写"1 index"，兜不住（该用例里 tool_use 恰好是 0 号块）。
> 改法：定死「`index` = 本流中 `tool_use` 块的出现顺序，从 0 开始；`input_json_delta` 与 `content_block_stop` 的 `index` 均指该 `eventIndex`」，并补一条"thinking 块在前、tool_use 在后"的用例。

**MF-S4 §4.4（L188）Gemini 数组模式的切分规则不是可实现的边界**【字节】
> 原文：L188「MUST 回退到数组模式：按 `[\s*,\s*]` 边界流式切出 JSON 对象（需增量 JSON 解析器，不得累积全量再解析）。」
> 解读 A：把 `[\s*,\s*]` 当正则字符类，按空白、`*`、`,` 切分 → `{"text":"a,b"}` 被切断，`*` 也会触发切分。解读 B：按 JSON 值的括号深度与字符串状态机切分（唯一能通过 GEM-002 的读法）。两种实现的 GEM-002 结果不同，且 A 读法根本无法产出合法对象。
> 改法：改写为「按顶层 JSON 值的边界切分：跟踪 `{}`/`[]` 深度与字符串/转义状态，深度归零即一个对象」，删掉正则字样。

**MF-S5 §4.3（L164）只认 `event:` 分派，与 CASES ANT-006 冲突**【判据】
> 原文：L164「请求 `stream: true`。`event:` 字段是分派依据，`data:` 是 JSON。」
> CASES L71：`ANT-006 | event: 行缺失（只有 data），以及 event: 与 data: 顺序颠倒 | 仍能正确分派（按 data.type 兜底）`。
> 解读 A：严格按 §4.3，没有 `event:` 就无法分派 → 该帧被丢弃或报 `PARSE_SSE_INVALID`。解读 B：实现 ANT-006 要求的 `data.type` 兜底。只读 stream-events 的执行者会实现 A 并挂掉用例。
> 改法：在 §4.3 补一句「`event:` 缺失时 MUST 按 `data.type` 分派，两者冲突时以 `data.type` 为准（记 trace）」。

**MF-S6 §3.2（L99）`notice.params` 的类型自相矛盾**【字节】
> 原文：L99「`params` 为字符串映射（如 `{"dropped":["vision","tools"]}`）」；`providers.md` L119 要求 `notice(capability_stripped, {dropped:[...]})`。
> 解读 A：`params` 是 `map<string,string>`，`dropped` 必须序列化成字符串（`"vision,tools"` 或 JSON 串）。解读 B：`params` 是 `map<string, any>`，`dropped` 是数组（例子和 providers.md 都是这个）。
> 改法：把"字符串映射"改为「`map<string, string|string[]>`」，例子即规范。

### SHOULD-FIX

- **SF-S1 I8（L114）与 §4.3 的 usage 合并冲突**：I8 说"一个事件 MUST NOT 跨两个 provider 帧被拆分"，但 §4.3 明确 `usage` 由 `message_start`（input）与 `message_delta`（output）两帧合并而成。改法：I8 改成"事件不得跨帧只发一半（半条 JSON）"，并把 usage 合并写成显式例外。
- **SF-S2 §5.5（L220）"追加一条 `notice` part" 的 `code` 未定义**：§3.2 的 `notice.code` 是六值闭集，没有"流被中断"这一项。两端只能各自发明字面量，UI 文案必然不同。改法：在闭集加 `stream_interrupted`（并同步 errors.md 与 ADR-003 §2.7）。
- **SF-S3 §4.2（L158）把 Responses 的截断终止挂在 `response.completed` 行**：`stop_reason 由 response.incomplete_details.reason 决定` 写在 completed 行里，而 §2.3（L55）的终止标记清单只列 `response.completed`。若真实端点以独立事件报告截断，照表实现会落到 `PARSE_INCOMPLETE`。改法：终止标记清单补上该事件名，并加一条截断用例。
- **SF-S4 §4.3 未列 `redacted_thinking`、`citations_delta`、`server_tool_use` 三类块**：闭集外的 `content_block_start` 该忽略还是发 `reasoning_delta`（前者安全，后者会把 base64 当思考文本上屏）没有规定。改法：加一行"未列出的 block 类型 MUST 忽略并记 trace"。

### 已验证、未发现问题的点

- §2.1 规则 1-9（有状态 UTF-8、`\n`/`\r\n`/`\r`、`:` 心跳、`field: value` 去一空格、BOM）与 FRAME-001/002/003/007/008 一一对应，无冲突。
- §2.2 的 `connect=10s`、`read_idle=180s` 与 `v1.sql` 的 `DEFAULT 10000` / `DEFAULT 180000` 一致；心跳重置 `read_idle` 与 ERR-006/FRAME-003 一致。
- I2/I3/I5 与 §4.1 `[DONE]` 处理（L142）、§5.1 取消路径（L216）、FRAME-005/006 一致，不互斥。
- I10（取消 → `done{aborted_user}`）与 ADR-003 §2.4、CAN-001/002 一致。
- §2.4 压缩归传输层、`providers.md` 只发 `gzip`，两者不冲突（deflate 是被动接受）。

### 结论：**改后冻结**（6 条 MUST-FIX）

---

## 3. spec/errors.md（1.0.0-draft）

### MUST-FIX

**MF-E1 §3.5（L123）`INTERNAL` 的 `stage` 不在闭集内**【字节】
> 原文：L123「| `INTERNAL` | — | ❌ | 出现了未预期的内部错误 |…」；§1 声明 stage 是闭集；§2 表 L55「`stage` | 必填，见 §1」。
> 解读 A：发 `"stage": "—"`（照抄表格）。解读 B：发 `"stage": null` 或挑一个就近的（`persist`/`parse`）。`stage` 进入 `failed.error`，即进入 `events.jsonl` → SHA 不同。
> 改法：给 `INTERNAL` 定一个真实 stage（建议 `local`，同时把它加进 §1 闭集），删掉 `—`。

**MF-E2 §2（L67）`trace_id` 必填随机 UUIDv7，与 CASES §0（L19）的双端 SHA-256 判据互斥**【判据】
> 原文：L67「`trace_id` | 必填；本地 UUIDv7，用于关联本地 trace 文件」；CASES L19「判据 (1) 双端 `events.jsonl` 的 SHA-256 相同；(2) 与 `expected.jsonl` 相同」。
> 解读 A：每个 attempt 生成新的 `trace_id` 写进 `failed.error`（符合 errors.md）→ 双端 SHA 永不相等，`expected.jsonl` 也永不命中，所有含 `failed` 的用例（ERR-001~006、OAI-007、ORT-003、RESP-005、ANT-005）永远红。解读 B：为过 gate 而不写 `trace_id` → 违约。
> 改法：二选一并写死：①runner 比对前从事件里剔除 `trace_id`（申明为"非确定性字段"）；②`trace_id` 改为从 `meta.json` 注入的确定性值。推荐 ①，并在 CASES §0 的判据里写明剔除清单。

**MF-E3 §3 各表的"可点操作"列与 §4 的 action 闭集无法对映**【字节】
> 原文：L75 `NET_OFFLINE` 操作「重试<br>打开网络设置」；L97 `MODEL_NOT_FOUND` 操作「刷新模型列表<br>手动指定模型」；L121 `DB_WRITE_FAILED` 操作「清理空间<br>数据库自检」；L100 `CAPABILITY_UNSUPPORTED` 操作「切换模型<br>移除图片/工具<br>查看能力矩阵」；L110 `SERVER_ERROR` 操作「…<br>查看服务状态」。§4 闭集（L129-145）里没有：打开系统网络设置、刷新模型列表、清理空间、查看能力矩阵、查看服务状态。
> 解读 A：把没有对应项的散文丢掉（`actions` 少一项，`actions` ≥1 仍满足）。解读 B：自造 `a` 值（`open_network_settings`、`refresh_models`…）。`actions` 数组进入 `failed.error`，两端字节不同；且 §2 要求"UI MUST 全部可点"，自造的 `a` 前端无法实现。§4 也没有"新增 action"的流程（§3 只规定了新增 code 的流程）。
> 改法：把每条 code 的操作列改成 §4 的 `a` 名字（`retry`/`check_proxy`/`switch_model`/`db_check`…），并给确实缺失的补 action 或删掉该散文操作；同时在 §4 补"新增 action 需 spec 升版本"。

**MF-E4 §4（L140）`top_up` 的目标 URL 没有来源，但 ERR-002 强制要求它**【实现】
> 原文：L140「`top_up` | 用系统浏览器打开 provider 计费页（URL 由 provider 配置提供，不内置硬编码）」；`providers.md` §1 `ProviderConfig`（L17-30）与 `v1.sql` `provider` 表（L45-69）都没有计费页字段；CASES L100 `ERR-002` 断言 `failed(AUTH_INSUFFICIENT_QUOTA)` 的 action 含 `top_up`。
> 解读 A：`top_up` 无法构造 URL，执行时降级为打开 provider 列表（等于把闭集 action 实现成另一个 action）。解读 B：自行在 DB 加 `billing_url` 列（违反"双端共用这一份 DDL"，且导出格式没有该字段）。
> 改法：在 `providers.md` §1 与 `v1.sql` 加 `billing_url TEXT`，或把 `top_up` 改成"打开 provider 编辑页并展示文案"（不带 URL）。两者择一，并让 export schema 跟上。

**MF-E5 §7（L213-224）脱敏流水线的顺序未定义**【字节】
> 原文：L218「`Authorization` 头、`x-api-key`… 整体替换为 `[redacted]`」；L218「任何形如 provider key 的串（`sk-[A-Za-z0-9_-]{16,}`…）保留首 4 + `…` + 尾 4」；L223「截断 | ≤512 字节，按 UTF-8 字符边界」。
> 解读 A：先按 header 规则 → `Authorization: [redacted]`；先按 key 模式 → `Authorization: Bearer sk-a…1234`。两种顺序对同一输入产出不同 `raw`。另：先截断再脱敏 vs 先脱敏再截断，会在 512 字节边界处留下不同的尾部（前者可能截出半个 key）。`raw` 在 `failed.error` 里，直接进 `events.jsonl`。
> 改法：写死管线顺序「结构字段替换（header/query/cookie）→ 模式脱敏 → 邮箱/路径 → UTF-8 边界截断到 512 字节」，并补一条"header 内含 key"的测试向量。

**MF-E6 §3 闭集（L69）缺少 `IMPORT_*` 三个 code，而导出契约与用例都在用**【判据】
> 原文：L69「`code` | 必填，见 §3 闭集。新增 code 需 spec 升版本」；`export-v1.notes.md` L36-40 用 `IMPORT_TOO_LARGE`、`IMPORT_MALFORMED`、`IMPORT_UNSUPPORTED_VERSION`；CASES L140 `EXPORT-004` 断言 `IMPORT_UNSUPPORTED_VERSION`。
> 解读 A：按闭集，导入失败只能借用 `INTERNAL`/`PARSE_SSE_INVALID` → 用例必挂。解读 B：扩展到三个新 code → 违反"闭集 + 升版本"。
> 改法：把三个 `IMPORT_*` code（含 stage 建议 `persist`、中文标题、causes、actions）正式加入 §3 并升版本号。

### SHOULD-FIX

- **SF-E1 L81-82**：`retryable` 写成「✅（仅首字节前）」，但 §2 定义它是必填布尔。按字面读是"运行时再判断"，按表头读是"常量 true"。改法：`retryable` 列只写 `✅`/`❌`，另加一列"仅在首字节前可重试"，并在 ERR-005 断言 `retryable=false`。
- **SF-E2 §5 无"默认"分支**：402（OpenRouter 余额不足）、408、409 等状态没有映射，也没写兜底规则。改法：在每个子表末加「其余状态 → `UNKNOWN_PROVIDER_ERROR`」，并把 402 映射到 `AUTH_INSUFFICIENT_QUOTA`。
- **SF-E3 §8 计数错误（L231）**：写"28 个 code、14 个 action"，实际是 29 个 code（含 `INTERNAL`）、15 个 action。改法：改成实数，否则后续"是否新增"无法核对。
- **SF-E4 §4 `open_docs {topic}` 的 topic 枚举未定义**，两端会给出不同的内置帮助页。

### 已验证、未发现问题的点

- §1 stage 闭集与 §3 各 code 的 stage 列除 `INTERNAL` 外全部吻合。
- §5.1/5.3/5.4 的映射与 `stream-events.md` §4.1/4.3/4.4 的 `finish_reason` 处理不冲突（`SAFETY` 有部分内容时走 `done{content_filter}`，两端一致）。
- §2 `causes` 1~3 条、按可能性降序，与 §3 每行的"候选原因"数量（2~3）相容。
- §7 的 key 前缀清单（`sk-`、`AIza`、`sk-ant-`、`gsk_`、`xai-`）与 ADR-002 §5 的扫描清单一致。

### 结论：**改后冻结**（6 条 MUST-FIX）

---

## 4. spec/providers.md（1.0.0-draft）

### MUST-FIX

**MF-P1 §3.1（L107）Anthropic 的 `temperature`/`top_p` 互斥后该发哪个未定**【字节】
> 原文：L106 `temperature`「同（thinking 开启时必须 1）」；L107 `top_p`「同（与 temperature 互斥：只发一个）」。
> 解读 A：canonical 里两者都非 null 时，优先发 `temperature`、丢 `top_p`。解读 B：优先发 `top_p`、丢 `temperature`。第三种：报 `PARAM_INVALID`。三者请求体字节不同，`request-golden`（§8 判据 4）直接失败。
> 改法：写死优先级（建议"有 `temperature` 则发 `temperature`，否则发 `top_p`"），并说明被丢的那个是否发 `notice(capability_stripped)`。

**MF-P2 §3.1（L114-115）`budget_tokens` 与 `effort` 互相映射缺表，且两字段落到同一 provider 字段**【字节】
> 原文：L114 `reasoning.effort` → Gemini `thinkingConfig.thinkingBudget`；L115 `reasoning.budget_tokens` → openai_chat「忽略 → 映射 effort」、Gemini 也是 `thinkingConfig.thinkingBudget`。
> 解读 A：canonical 的 `reasoning.mode` 决定用哪个（`effort` 用 effort，`budget` 用 budget）→ 一致。解读 B：两个字段都非 null 时，Gemini 的 `thinkingBudget` 取谁？openai 的 `budget_tokens=8000` 该映射成 low/medium/high 中的哪一个？spec 里只有 effort→budget 的正向表（§3.4：1024/4096/16384），没有反向表。
> 改法：补一张 `budget_tokens → effort` 的反向区间表，并写死"`mode` 优先于字段存在性"；Gemini 行注明"`mode='effort'` 时用 effort 映射值"。

**MF-P3 §1（L17-30）`first_byte` 与 `total` 超时声明为 provider 可配，但配置模型与 DDL 都没有字段**【实现】
> 原文：`stream-events.md` §2.2（L47-49）「`first_byte` | 60s | …（provider 级可配，上限 300s）」「`total` | 无默认上限 | provider 级可配」；`providers.md` §1 `ProviderConfig` 只有 `connect_timeout_ms, read_idle_timeout_ms`；`v1.sql` `provider` 表同样只有这两列。
> 解读 A：两处超时用硬编码常量（则"provider 级可配"是空话，且 300s 上限无处校验）。解读 B：自行加列（各端字段名不同，导出格式也没有）。同一长思考模型的 `first_byte` 行为因此不同。
> 改法：在 `ProviderConfig`、`v1.sql`、`export-v1.json` 三处同时补 `first_byte_timeout_ms`（默认 60000，上限 300000）与 `total_timeout_ms`（默认 0=不限）。

**MF-P4 §3.1（L117）`cache.breakpoints` 在 OpenAI/Gemini 上的 `notice` 没有 `code`**【字节】
> 原文：L117「`cache.breakpoints` | 无操作（自动前缀缓存）+ `notice` | 同 | 在对应内容块加 `cache_control` | 无操作（cachedContent 需单独创建，v1 不做）」。
> 解读 A：发 `notice(capability_stripped,{dropped:["cache"]})`（§3.1 剥离规则要求"任何被剥离的字段 MUST 产生 notice"）。解读 B：发 `notice(provider_downgraded)`。解读 C：不发（因为"无操作"不是剥离）。`notice` 是事件，多一条就改变 `seq` 编号与 `events.jsonl` 字节。
> 改法：写明该行"不产生事件"（自动前缀缓存是 provider 行为，不是能力缺失），或指定唯一 code。二选一。

### SHOULD-FIX

- **SF-P1 §1（L48）正文残留未决问句**：「→ 不报错，但返回 `notice(provider_downgraded)`? 否：交由 `ENDPOINT_NOT_FOUND` 呈现」。规范句里不许有 `?`，照前半句实现会"发 notice 且不报错"。改法：删掉前半句，只留"交由 `ENDPOINT_NOT_FOUND` 呈现"。
- **SF-P2 §5（L208）分词器选型留到 M2，但同时要求偏差 ≤1%**：`com.kmnv.jtokkit` 与 `Microsoft.ML.Tokenizers` 对中文语料的偏差大概率超 1%，"MUST 相同"不可执行。改法：现在定一个双端共用方案（或由服务端模型元数据提供 token 数），或把措辞降为"偏差入账，不阻塞"。
- **SF-P3 §4（L196）"模型列表来自本地缓存"的 `notice` 没有 code**，同 MF-P4 的问题；若这不是 StreamEvent 而只是 UI 提示，需在文中区分"StreamEvent.notice"与"UI 提示"。
- **SF-P4 并发上限**：`v1.sql` 的 `concurrency_limit` 是 **每 provider**（默认 4），ADR-003 §4 写"全局默认 4 个活跃流（可在 provider 级调整）"。"全局 4"没有落地字段（只能塞进 `app_setting`，未声明键名）。改法：声明全局键名与默认值。
- **SF-P5 §3.5**：Gemini 的 `?alt=sse&key=<key>` 与"优先头方式"并存，同时 `extra_query` 也可能带 `alt`，拼接顺序与去重未规定（会影响 `request-golden`）。改法：写明查询串的固定顺序与冲突优先级。

### 已验证、未发现问题的点

- §1 六种 protocol 的默认 `base_url` 与 §3 各节的 path 拼接结果一致（含 Gemini 的 `{model}:streamGenerateContent`）。
- §2 的 part 闭集（`text|image|file|tool_call|tool_result`）与 §3.1 映射表完全覆盖，无遗漏类型。
- §3.1 `max_output_tokens` 在 Anthropic 必填、`v1.sql` 有 `max_tokens_param`，与 `stream-events.md` 无冲突。
- §3.4 thinking 与采样参数互斥、"MUST 强制 temperature=1" 与 MF-P1 的结论一致（两者不矛盾：一个是 thinking 开启，一个是默认）。
- §7 的"3xx MUST NOT 自动跟随"与 `errors.md` 的 `ENDPOINT_NOT_FOUND` 相容。

### 结论：**改后冻结**（4 条 MUST-FIX）

---

## 5. spec/render-model.md（1.0.0-draft.2）

### MUST-FIX

**MF-R1 §5（L88）"嵌套深度超限降级为 `text`"中的 `text` 不是 Block 类型**【字节】
> 原文：L88「嵌套深度（list/quote） | 32 | 超出部分降级为 `text`」；§2 的 Block 闭集（L35-51）是 13 种，其中没有 `text`（`text` 只在 §3 的 Inline 里）。
> 解读 A：降级为 `paragraph{inlines:[text{...}]}`，保留原始标记字符。解读 B：降级为 `unknown{kind:"list"|"quote", raw}`。解读 C：降级为 `code{text}`。三者都是"照 spec 做"，L1 金样本逐字段 diff 直接失败。
> 改法：改为「降级为 `unknown{kind:"list"|"quote", raw:<原始标记>}`」，与 §2 `unknown` 的"前向兼容、等宽渲染"语义一致。

**MF-R2 §2（L50）与 §3（L63）没有"图片成块还是成行内"的判定条件**【字节】
> 原文：L50 Block `image`「独占一行的图片」；L63 Inline `image`「`src`, `alt`, `title`」。
> 解读 A：段落里只有一个 `![alt](url)` 时提升为 Block `image`。解读 B：永远是 `paragraph{inlines:[image]}`，Block `image` 只在进入时不产生段落时使用（那它何时出现？）。"独占一行"不是可判定条件：`图片 ` 前有空格、有尾随文字、行首缩进 1 空格，三种边界都无规则。
> 改法：写死「段落内联表达式只有一个 `image` 且该段落前后无其他 inline 时，产 Block `image`；否则产 Inline `image`」，并补一条 RENDER 用例（现有 70 条里没有任何图片用例）。

**MF-R3 引用式链接与链接定义不在任何闭集内，且与 §4.5 前缀稳定性互斥**【字节】
> 原文：§2 Block 闭集（L35-51）无"链接定义"块；§3 Inline 的 `link` 只有 `dest`；§4.5（L76）「`parse(p).blocks` 除**最后一块**外，MUST 与 `parse(s).blocks` 的对应前缀逐字段相同」。
> 输入：`看这个 [文档][d]`，然后追加 `\n\n[d]: https://x`。解读 A：支持引用式链接 → 追加定义后**前面**的段落 inline 由 `text` 变成 `link`（非最后一块变化）→ 违反 §4.5。解读 B：不支持 → `[d]: https://x` 落进 Block `unknown`（则它成为新的最后一块，前一块仍变化，同样违反 §4.5）或直接丢弃。三种读法产生三种 RenderModel。
> 改法：明写「v1 不解析引用式链接与链接定义：`[x][y]` 原样为 `text`，`[y]: url` 行归入 `unknown`」，并把"追加定义不改变已完成块"加入 §4.5 的说明。

### SHOULD-FIX

- **SF-R1 §3（L53）"共 9 种"与表内 10 行不符**（`text/emph/strong/strike/code/link/image/math/footnote_ref/br`）。改法：改为 10 种；`br` 没有任何用例覆盖，顺带补一条"两个空格换行 + `<br>`"用例。
- **SF-R2 §2（L41）`code.meta:{filename, line_numbers}` 的来源未定义**：围栏 info string 的语法（```` ```python filename=app.py ln ````？）没有规定，两端解析出的 `meta` 必然不同。改法：给出 info string 的 BNF 或直接删除 `meta` 的 `filename`/`line_numbers`。
- **SF-R3 §1 的 `flags` 缺 `has_list`/`has_footnote`**，而列表与脚注都会影响虚拟化与折叠决策；两端会自行扩充字段（`additionalProperties` 未声明为 false）。
- **SF-R4 §4.7（L78）"≤2ms"与 §6（L99）"≤2ms"重复且判据不同**（一个针对"最后一个开放块"，一个针对"重解析"），建议合并为一处。

### 已验证、未发现问题的点

- Block 13 种的计数与表内行数一致（`paragraph/heading/code/math/mermaid/table/list/quote/hr/html/footnote_def/image/unknown` = 13）。
- §2 `mermaid.kind` 与"只渲染 flowchart/sequence、其余降级代码块"与 RULINGS-2026-11-22 第 5 条、ADR-001 §2.4 完全一致；`other` → 降级为 `code` 的措辞也一致（不违反"禁用尽力支持"）。
- §8 的四层等价（L1 0 差异 / L2 0 差异 / L3 2dp·2px / L4 12 条脚本）与 ADR-001 §5、RULINGS 第 2 条逐项吻合；L4 脚本清单数目两端都是 12 条，条目一一对应。
- §4.3（不闭合数学原位切换、同一块 id）与 §4.6（块 id `b0,b1…`）不冲突：`closed=false→true` 改变的是块内容而非 id。
- §7 的链接协议白名单与"HTML 一律转义"与 RENDER-011 期望一致。

### 结论：**改后冻结**（3 条 MUST-FIX）

---

## 6. spec/schema/v1.sql

### MUST-FIX

**MF-V1 `message_part`（L250-262）没有 `truncated` 列，但 ADR-003 要求"把已收到的 `message_part` 标记 `truncated=true`"**【实现】【判据】
> 原文：`message_part` 的列是 `id, message_id, part_index, kind, body, meta_json, created_at, updated_at`（L251-259），无 `truncated`；只有 `message.truncated`（L239）。ADR-003 §2.3「取消时把已收到的 `message_part` 标记 `truncated=true` 并落盘」；CASES L111 `CAN-002` 断言"DB 中该 message 为 `aborted` 且 `truncated` 按实际情况置位"。
> 解读 A：只能用 `message.truncated`（则 ADR 的"per-part"语义丢失，且同一消息里"完整的前半段"与"被截断的后半段"无法区分）。解读 B：给 `message_part` 加列（DDL 是双端共用契约，两端同时加才不冲突，但导出 schema 没有该字段 → EXPORT 往返丢字段）。照 ADR 做在现有 DDL 上不可能。
> 改法：二者择一并同步三处（DDL / export schema / ADR-003 §2.3 的措辞）：推荐给 `message_part` 加 `truncated INTEGER NOT NULL DEFAULT 0`。

**MF-V2 `provider`（L45-69）缺 `first_byte_timeout_ms`、`total_timeout_ms`、`billing_url`**【实现】
> 原文：列只有 `connect_timeout_ms`、`read_idle_timeout_ms`、`max_retries`、`concurrency_limit`。而 `stream-events.md` §2.2 声明 `first_byte`/`total` 可 provider 级配置，`errors.md` §4 声明 `top_up` 的 URL 来自 provider 配置。
> 解读 A：两端各加列（列名、默认值、CHECK 范围各异）→ "双端共用这一份 DDL" 失效，M0-QE-001 的 schema-diff 必失败。解读 B：塞进 `app_setting`/`extra_json`（无契约，导出后丢失）。
> 改法：同上三个字段进 DDL（并同步 `providers.md` §1 与 `export-v1.json`）。

### SHOULD-FIX

- **SF-V1 `message_attachment` 的主键含 `part_index`（L285），而 `export-v1.json` 把它设为可选**：导入时 `part_index` 缺省值未定义，同一 (message, attachment) 可能出现两行。改法：导出 schema 把 `part_index` 设为 required。
- **SF-V2 `conversation.active_leaf_message_id`（L213）是 TEXT 无类型约束**，而其余 id 在导出 schema 里是 `$ref: uuid`；不一致会让"非法 id"只在 DB 侧失败。
- **SF-V3 `conversation.settings_json`（L214）与 `agent_profile.sampling_json`（L137）的结构没有任何契约**：`providers.md` §2 的 canonical `sampling` 与它们是"覆盖"关系，但覆盖粒度（逐字段还是整体替换、null 如何解释）未定义 → 两端 `request-golden` 会产生不同 body。

### 已验证、未发现问题的点

- 所有布尔列都带 `CHECK (x IN (0,1))`；所有时间戳统一 INTEGER 毫秒（含 `schema_meta.created_at` 的 `||'000'` 换算）。
- `provider.protocol` 的 6 值 CHECK 与 `providers.md` §1 的 `protocol` 枚举逐字一致。
- `message_part.kind` 的 7 值 CHECK 覆盖 `stream-events.md` §3.4 的 `kind='reasoning'` 与 §5.5 的 `notice` part。
- `model` 表的 `supports_*` 六列与 `providers.md` §6 的能力矩阵（`vision, tools, thinking, cache, json_schema, long_context`）中可本地化的五项对应；`long_context` 由 `context_window` 推导，不冲突。
- `usage_record` 的 `cost_estimate`/`provider_cost`/`is_estimate`/`error_bound_pct` 与 `stream-events.md` §3.2 的 `usage` 字段及"估算必须标注"的要求一致。
- `tool_call_record.call_index` 与 `stream-events.md` 的 `index` 对应关系写明了（L346），无歧义。
- 软删除字段齐全：`provider/provider_key/model/agent_profile/prompt_template/memory_item/folder/conversation/message/attachment/mcp_server` 均有 `deleted_at`；无 `deleted_at` 的表（`message_part`、`tag`、`conversation_tag`、`usage_record`、`mcp_tool`、`tool_call_record`、`app_setting`）在导出 schema 里同样没有——**两侧一致，未发现单边软删除字段**。
- `schema_meta`/`migration`/`message_fts` 不导出：`message_fts` 是派生索引、前两者是库元数据，不违反无损定义。

### 结论：**改后冻结**（2 条 MUST-FIX）

---

## 7. spec/schema/export-v1.json + export-v1.notes.md

### MUST-FIX

**MF-X1 notes §3（L27）要求的 `content_omitted=true` 字段在 schema 里不存在，而 schema 是 `additionalProperties:false`**【判据】
> 原文：notes L27「超限的附件降级为元数据并在 `attachment` 上标 `content_omitted=true`（该字段仅在 inline 模式出现，schema 允许 `content_b64` 缺失）」；`export-v1.json` 的 `attachment`（L392-414）只有 `content_b64`，没有 `content_omitted`，且 `"additionalProperties": false`（L394）。
> 解读 A：按 notes 写 `content_omitted` → 文件被自己的 JSON Schema 拒绝（notes §4 要求"必须通过本 JSON Schema"，失败即拒绝导入）。解读 B：不写 → 违反 notes §3，导入端无法区分"字节没带"与"超限没带"。
> 改法：在 `attachment` 加 `"content_omitted": {"type":"boolean"}`，或把 notes 的措辞改为"用 `content_b64` 缺失 + `size_bytes` 判定"。

**MF-X2 `secrets` 的 AAD 有三份不同定义，KDF 下限低于 ADR-002（跨端解密必失败）**【判据】
> 原文：`export-v1.json` L105「AAD = 本文件 app/options/kdf/cipher 的规范化 JSON」；notes L50「AAD = `{format, format_version, app, options, secrets.kdf, secrets.cipher}` 的规范化 JSON」；ADR-002 L30「AAD = 导出文件头明文（版本 + 算法 + 参数 + **创建时间**）」。
> 三份集合不同（notes 多了 `format`/`format_version`，ADR 多了"创建时间"且 schema 里没有这个字段）→ 同一密文在两端用不同 AAD 校验 GCM tag，必然失败。另：schema `iterations` 下限 100000（L90），ADR-002 L29 要求 PBKDF2 ≥600000，即 schema 允许生成违反裁决的文件。
> 改法：以 notes §5 为唯一 AAD 定义（写进 schema 的 description 与 ADR-002 的落地映射），并把 `iterations` 的 `minimum` 提到 600000。

**MF-X3 DDL 与导出 schema 的字段覆盖/必填不一致（四处）**【判据】
> ①`v1.sql` `prompt_template`（L152-165）与 `memory_item`（L168-184）有 `sync_version`/`rev`/`origin_device`，`export-v1.json` 的 `prompt`（L262-277）与 `memory`（L278-295）没有，且都 `additionalProperties:false` → 导出包含即被拒、不含即丢字段。②`mcp_server` 有 `last_check_at`/`last_check_ok`（L323-324），导出 `mcp_server`（L449-467）没有。③`attachment.local_path` 是 `NOT NULL`（L272），但不在 `required`（L395）→ 能通过校验的文件无法入库。④`provider`/`model`/`conversation`/`message` 的 `rev` 都是 `NOT NULL`，都不在各自 `required` 里。
> 旁证：`$defs.sync_fields`（L139-146）与 `$defs.timestamps`（L147-154）已定义却**从未被任何 `$ref` 引用**——正是漏掉这批字段的原因。
> 改法：把 `sync_fields`/`timestamps` 用 `allOf` 组合进所有带这三个字段的实体，并把每个实体的 `required` 与 DDL 的 `NOT NULL` 列逐一对齐（建议加一个"schema↔DDL 一致性"的自动检查任务）。

**MF-X4 notes §6.1（L55）容器格式仍未决，而它决定磁盘上导出文件的字节**【判据】
> 原文：「导出文件是单个 JSON（内嵌 base64）还是 zip 包（JSON + 附件目录）？…**建议：默认单 JSON（≤200MB），超过 200MB 时自动切 zip。**」
> 解读 A：单 JSON（schema 只描述这一种）。解读 B：按大小自动切 zip → 导入端必须先探测容器（zip magic vs `{`），而 schema 里没有任何容器字段；两端必然对同一份导出文件给出不同结论。
> 改法：现在就定"v1 只有单 JSON"，把 zip 移入 §7「v1 明确不做」，并把"2GB 上限 + 流式 parser"写成硬性约束（或把上限降到 200MB）。

### SHOULD-FIX

- **SF-X1 `message_attachment` 的 `part_index` 非必填**（与 SF-V1 同源）。
- **SF-X2 `provider_key.key_ref` 不导出**（正确），但导入时如何生成新 `key_ref`、`UNIQUE(provider_id,key_ref)` 冲突如何处理，未写。改法：注明"导入时逐行新建密钥库条目，`key_ref` 重新生成"。
- **SF-X3 `options.scope='settings'` 时 `data` 仍 `required` 五个集合**（L114），只能填空数组；不算错，但应写明"空集合必须存在"。
- **SF-X4 notes §6.2/§6.3**（`settings` 是否含设备相关键、是否要轻量 Markdown 导出）仍是待决项，同样阻塞"可冻结"。

### 已验证、未发现问题的点

- 字段名与 `v1.sql` 列名逐字一致（无 camelCase 映射层），与 notes §1 的取舍说明相符；抽查 `provider/provider_key/model/agent/conversation/message/message_part/usage_record/tool_call/setting/tombstone` 全部对得上。
- 类型映射一致：DDL `INTEGER 0/1` → JSON `boolean`；`REAL` → `number`；`TEXT` → `string`；可空列 → `["T","null"]`。抽查 `health_score`、`is_estimate`、`error_bound_pct`、`pass_through`、`truncated` 均正确。
- `format`/`format_version` 用 `const` 钉死，`EXPORT-004`（`format_version: 2`）因此必然被 schema 拒绝——与用例一致。
- `integrity.counts`/`sha256` 的结构与 notes §2 的"逐字段相同"判据相容（排除 `exported_at`/`integrity` 后比较 `data`）。
- 排序要求（notes §2.5"按主键升序"）与 UUIDv7 时间有序的设计自洽。

### 结论：**改后冻结**（4 条 MUST-FIX）

---

## 8. spec/conformance/CASES.md（70 用例）

### MUST-FIX

**MF-C1 §0（L19）的判据与 `errors.md` 的 `trace_id` 互斥（所有 `failed` 用例永不通过）**【判据】
> 原文：L19「(1) 双端 `events.jsonl` 的 SHA-256 相同；(2) 与 `expected.jsonl` 相同（若提供）」；`errors.md` L67「`trace_id` | 必填；本地 UUIDv7」。
> 解读 A：`failed` 事件里带运行时生成的 `trace_id` → 判据 (1) 双端不相等、(2) 与固定 `expected.jsonl` 也不相等 → ERR-001~006、OAI-007、ORT-003、RESP-005、ANT-005 八条用例结构性失败（G1/G2 全红）。解读 B：不带 → 违反 errors.md。
> 改法：在 §0 增加"非确定性字段清单（`trace_id`，如适用 `request_id`），比对前剔除"，并同步改 `errors.md` §2 注明该字段不参与 conformance 比对。

**MF-C2 L49 OAI-009 的期望不可判定，且与 I11/§4.1 冲突**【判据】
> 原文：L49「同一内容 chunk 被完整重发 2 次（重复事件） | 文本不重复（适配器须按 `choice.0.delta.content` 去重或用 seq 判断）；**事件数符合期望**」。
> 解读 A：按内容去重 → 只发 1 个 `delta`（2 个事件 vs 3 个事件）。解读 B：原样发 2 个（§4.1 的映射就是"`delta.content` → `delta`"，且 I11 的"前缀差集"只对 Gemini 定义了算法）。"事件数符合期望"没有给出数字，无法判定；更糟的是，合法的重复文本（模型连发两个 "ha"）与"重发"在流上不可区分，A 读法会吞掉真实内容。
> 改法：把该用例改成可判定形式：样本里两次 chunk 内容相同且**用 `meta.json` 明确标注 `duplicate: true`**，期望写死"1 个 `delta`，事件总数 = N"，并在 `stream-events.md` §4.1 补一行"重复 chunk 的去重判据"。

**MF-C3 L111 CAN-002 的"`truncated` 按实际情况置位"与 ADR-003 §2.3 冲突且不可判定**【判据】
> 原文：L111「DB 中该 message 为 `aborted` 且 `truncated` 按实际情况置位」；ADR-003 §2.3「取消时把已收到的 `message_part` 标记 `truncated=true` 并落盘」。
> 解读 A：取消一律 `truncated=true`（照 ADR）。解读 B：只有"取消发生在流中途且已有内容"才 true，"首字节前取消"为 false（照"按实际情况"）。ADR 的措辞是无条件的，A 与 B 对 CAN-001（首字节前取消）给出相反断言。
> 改法：写死"用户取消 → `truncated=true`（无论是否已有 delta）；`truncated` 只在 `status='aborted'` 或超窗/超长时为 true"，并让 ADR-003 §2.3 与 DDL（MF-V1）对齐。

**MF-C4 不变量 I6、I8 没有任何用例，而 `stream-events.md` §6（L226）声称"每条不变量都对应用例"**【判据】
> 原文：§6（L226）「本文件的每条不变量都对应 `spec/conformance/CASES.md` 中的用例」；70 条里：I6（空文本 MUST NOT 发射）无、I8（事件不跨帧拆分）无、I11 只在 GEM-003 覆盖（OpenAI/Anthropic 的重复前缀无）；I12 只有 CAN-004（仅取消路径）。
> 解读 A：一个"收到 `delta{text:""}` 就照发"的实现能 70 条全绿 → 判据形同虚设。解读 B：执行者按 §6 自己补用例，补的样本不在 spec 里 → 双端样本不同。
> 改法：补 4 条用例（`FRAME-011` 空 `delta` 抑制 → I6；`OAI-011` 单帧产多事件的计数 → I8；`OAI-012` 重复前缀去重 → I11；`FRAME-012` `done` 后 socket 计数 → I12），或把 §6 改成"见 §11 覆盖矩阵"，并如实列出未覆盖的不变量。

### SHOULD-FIX

- **SF-1 §0 判据 (3) 只认 `expect_error`**，但 FRAME-003（idle 重置）、CAN-003/004（DB 与 socket 断言）、RENDER-012（≤2ms）等期望无法表达在 `events.jsonl` 里；这些用例的"通过"目前靠人工。改法：`meta.json` 增加 `asserts:{db,socket,perf}` 段并规定 runner 输出。
- **SF-2 "样本形态：`stream.sse`（原始 HTTP body 字节，未解压）"**（L15）与 `stream-events.md` §2.4 的 gzip 透明解压之间没有 gzip 样本；该分支无验收。
- **SF-3 `chunk_pattern` 的取值（`bytes:1`/`random:seed`/`whole`/`fixed:37`）只有四个示例**，未规定语法与默认值；缺失时应默认 `whole` 要写明。
- **SF-4 EXPORT-003 断言"三种都被拒绝并给出具体错误路径；DB 无任何写入"**，但"错误路径"的格式（JSON Pointer？点号？）未定义，两端字符串必然不同。改法：钉死为 JSON Pointer（如 `/data/attachments/3/local_path`）。

### 已验证、未发现问题的点

- 用例总数核对：FRAME 10 + OAI 10 + RESP 5 + ANT 8 + GEM 6 + ORT 4 + ERR 6 + CAN 4 + RENDER 13 + EXPORT 4 = **70**，与标题一致。
- 每条用例的期望与对应 spec 条款逐条对读，未发现除上述之外的第二处冲突：FRAME-004↔§2.3 宽容路径、FRAME-005/006↔I2/I3、OAI-003/004↔I5/I7、ANT-005↔§4.3 L184、GEM-006↔errors §5.4、ORT-002↔§3.2 `provider_cost`、ERR-001/003↔errors §5.1、RENDER-005/006/011↔render §2/§7。
- §11 的 Gate 归属（G0 runner / G1 FRAME·OAI·ERR·CAN / G2 +RESP·ANT·GEM·ORT / G3 RENDER·EXPORT / G4 TOOL）自洽，不与"70 条全绿"冲突。

### 结论：**改后冻结**（4 条 MUST-FIX）

---

## 9. 跨文件冲突清单（同一事实在两份文件里不同）

| # | 冲突 | 两侧原文位置 | 后果 |
| --- | --- | --- | --- |
| 1 | `trace_id` 随机必填 vs 双端 `events.jsonl` SHA-256 相等 | errors.md L67 ↔ CASES.md L19 | 所有 `failed` 用例永久红（G1/G2 无法开） |
| 2 | `IMPORT_TOO_LARGE`/`IMPORT_MALFORMED`/`IMPORT_UNSUPPORTED_VERSION` 不在 code 闭集 | errors.md L69 ↔ export-v1.notes.md L36-40、CASES L139-140 | EXPORT-003/004 无合法错误码 |
| 3 | `message_part.truncated`：ADR 要求、DDL 无列、导出无字段、用例"按实际情况" | ADR-003 §2.3 ↔ v1.sql L250-262 ↔ export-v1.json L377-391 ↔ CASES L111 | 取消/截断落盘语义不可实现，三端三说 |
| 4 | `content_omitted`：notes 要求、schema 禁止 | export-v1.notes.md L27 ↔ export-v1.json L394 | 按 notes 导出的文件被自己的 schema 拒绝 |
| 5 | AAD 三种定义（含/不含 `format`、含/不含"创建时间"） | export-v1.json L105 ↔ notes L50 ↔ ADR-002 L30 | 跨端密钥导入解密必失败（违反 RULINGS 第 3 条的可互通前提） |
| 6 | `first_byte`/`total` 超时"provider 级可配"但无字段 | stream-events.md L47-49 ↔ providers.md L17-30 ↔ v1.sql L45-69 | 长思考模型首字节行为两端不同 |
| 7 | `top_up` 的 URL"由 provider 配置提供"但无字段 | errors.md L140 ↔ providers.md §1 ↔ v1.sql `provider` ↔ CASES L100 | ERR-002 要求一个无法执行的操作 |
| 8 | 10 MiB 单行载荷：合法 vs 超限 | stream-events.md L39-40 ↔ CASES L34 | FRAME-009 判据不可满足 |
| 9 | Anthropic `event:` 是唯一分派依据 vs 必须有 `data.type` 兜底 | stream-events.md L164 ↔ CASES L71 | ANT-006 与 spec 相反 |
| 10 | I11 前缀去重是全局不变量，但只在 Gemini 定义算法；OAI-009 又要求另一套去重 | stream-events.md L117、L199 ↔ L146 ↔ CASES L49 | OpenAI 重复 chunk 处置不可判定 |
| 11 | `notice` 事件没有"流中断"与"模型列表来自缓存"的 code（闭集） | stream-events.md L99 ↔ L220 ↔ providers.md L196 | 两端自造 code，文案与事件流不同 |
| 12 | "每条不变量都有对应用例"但 I6/I8 无用例 | stream-events.md L226 ↔ CASES 全文 | 覆盖判据不成立，非合规实现可全绿 |
| 13 | 全局并发默认 4 vs 每 provider 默认 4 | ADR-003 §4 ↔ v1.sql L57 | 并发上限语义两端不同 |
| 14 | 本地分词器"MUST 偏差 ≤1%"但选型留到 M2 且需 ADR | providers.md L208 ↔ 同节 M2 说明 | 误差指标不可承诺，估算值两端不同 |

## 10. 与 ADR / 人类裁决的一致性核查（逐条结论）

| 裁决/ADR | 核查结果 |
| --- | --- |
| RULINGS 1 启动三档 | 六份 spec 未涉及；ADR-001 §2.5 已按裁决拆三档。无违反。 |
| RULINGS 2 四层等价（L1/L2 零差异、L3 2dp·2px、L4 脚本） | `render-model.md` §8 与 ADR-001 §5 逐项一致，L4 12 条脚本两端对齐。无违反。 |
| RULINGS 3 仅口令导出（Argon2id + AES-256-GCM） | **有违反风险**：见 MF-X2——AAD 三份定义、PBKDF2 下限 100000 < ADR 的 600000，schema 允许生成违反裁决的文件。 |
| RULINGS 4 仅前台保证、客户端续写、悬挂流修复 | `stream-events.md` §5.5 与 ADR-003 §2.6/§2.7 一致（"继续生成"措辞、不自动重发、60s 修复）。**例外面**：`message_part.truncated` 无列（MF-V1）、中断 `notice` 无 code（SF-S2）。 |
| RULINGS 5 Mermaid 仅 flowchart/sequence、静态、降级代码块 | `render-model.md` §2/§4.4/§5 与 ADR-001 §2.4 完全一致，且"禁用尽力支持"被落实为 `kind="other"` 一律降级。无违反。 |

## 11. 我验证过但没发现问题的部分（避免后续重复劳动）

- `stream-events.md`：I1/I2/I3/I4/I5/I7/I9/I10 与 70 用例的对应关系成立；L1 分层（字节→帧→事件）在 §2 与 §3 之间无空洞；取消路径（socket 先关、再补 `done{aborted_user}`）与 ADR-003 §2.4 一致。
- `errors.md`：`stage` 闭集与 `code` 分类映射（除 `INTERNAL`）无歧义；`raw` ≤512 字节与 §7 截断一致；`request_id` 的三个来源（`x-request-id`/`request-id`/`x-request-id`）与 providers.md §7 的响应头白名单一致。
- `providers.md`：六种 protocol 的请求构造（path、header、body 骨架）与 §8 四条一致性判据相容；能力矩阵六项与 `v1.sql` 的 `supports_*` 无缺口。
- `render-model.md`：Block 13 种覆盖 70 条用例提到的全部语法（代码/列表/表格/数学/Mermaid/HTML/脚注/任务列表），唯一缺口是图片（MF-R2）与引用式链接（MF-R3）。
- `v1.sql`：软删除字段两端（DDL 与导出 schema）逐一比对，**没有**出现"一侧有 `deleted_at`、另一侧没有"的情况。
- `CASES.md`：70 条计数、Gate 归属、`meta.json` 的字段（状态/头/协议/config/终止原因）与 runner 输出契约自洽。

---

**总结论：七份文件全部「改后冻结」。最优先顺序：①`trace_id` × SHA 判据（MF-E2/MF-C1，阻塞一切 gate）；②§2.3 × §4.1 的终止分支（MF-S2）；③8 MiB 单行 × FRAME-009（MF-S1）。这三条修完之前，双端 `events.jsonl` 不可能逐字节相同，M1 的核心判据不成立。**
