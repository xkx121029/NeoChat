# NeoChat 契约独立评审 · 第二轮（复验）

- 评审对象：`spec/stream-events.md` 1.0.0-draft.3、`spec/errors.md` 1.0.0-draft.3、`spec/providers.md` 1.0.0-draft.3、`spec/render-model.md` 1.0.0-draft.3、`spec/schema/v1.sql`（1.0.0-draft）、`spec/schema/export-v1.json` + `export-v1.notes.md`、`spec/conformance/CASES.md` 1.1.0-draft（78 条）
- 待验清单：`docs/reviews/spec-review-1.md`（29 条 MUST-FIX）
- 变更依据：`spec/CHANGELOG.md`
- 评审者：独立评审（非作者），本机执行，未进云端
- 评审日期：2026-11-22
- 本轮的职责边界：只复验上一轮 29 条 MUST-FIX + 只查"改动修出来的新缺陷"。不重做已确认无问题的部分。

## 0. 只读证据

评审前后文件未变动，`git status --short` 无输出：

```
6fa86927d116225d9e5407d3885ef147646cdc1f7594a2c6674b5fa11e991d0f *spec/CHANGELOG.md
de3d48a299a5397fdbd6041636421b788cc116a8dc3969bdfd35f0cec91e41a5 *spec/errors.md
822ab1ed2ff9528b24e2e996f055af3924bafd15727255e7936f2f501f16102f *spec/providers.md
283fba439779d370bf92f1c574fd11a3e2087fa5f71e9846a8d4bfc2e8e8e92f *spec/render-model.md
1a552817c78fbd7786fcea566922bdc2339359329e5777f0da1c0aa93f25c327 *spec/stream-events.md
d58c13913ad362e5f7ab74426b9ecb09e9f7f1c6748bda1435773ba33b3fe561 *spec/conformance/CASES.md
7d61af0f41390df3e9f65a8aa68cb9a4744801bb19dc4aebd5f6b8927e15b8ba *spec/schema/export-v1.json
22a33d0be3b866766d8d43ddd06f15652f48fc3465ca3d852c235bfdc9d2c4c0 *spec/schema/export-v1.notes.md
694cad0062b7c9ffd8fb140decbff91aae581dbe681da53b6dc43627d335a3ed *spec/schema/v1.sql
```

`python tools/spec-lint/verify.py` → `=== RESULT: ALL PASS ===`（22 条 OK）。这个 ALL PASS 与本文 §2 的 5 条新 MUST-FIX 并存，原因见 §4——不是它跑错了，是它没有查。

---

## 1. 29 条 MUST-FIX 逐条复验

结论口径：**已关闭** = 现在有唯一正确读法且能找到原文；**未关闭** = 还差东西；**改偏了** = 修了但产生新的两种解读。

| # | 编号 | 结论 | 依据（文件 + 原文） |
| --- | --- | --- | --- |
| 1 | MF-S1 单行 8 MiB × FRAME-009 | **已关闭** | `stream-events` L39-40：规则 10 单帧 16 MiB；规则 11「**单行上限与单帧上限相同**（16 MiB）…一次 10 MiB 的合法单行 `data:` 必须被接受（FRAME-009 即此形态）」。两档用例反着写死了形态：`CASES` L37「**单行** `data:` 载荷 10 MiB…正常解析」／L38「20 MiB…`failed(PARSE_EVENT_TOO_LARGE)`」 |
| 2 | MF-S2 §2.3 × §4.1 终止分支相反 | **已关闭** | `stream-events` L146：「**字段缺失时不得映射为 `stop`**：EOF 一律回到 §2.3 判定（从未见过 `finish_reason` → `failed(PARSE_INCOMPLETE)`）」；§2.3 L56-58 保留宽容路径但限定为"已收到 `finish_reason`" |
| 3 | MF-S3 Anthropic `eventIndex` | **已关闭** | L171：`eventIndex` =「本流中 `tool_use` 块的**出现顺序**（从 0 起，text/thinking 块不占号）」；L174「按该 delta 所属 content block 的 provider `index` 反查 `eventIndex`」；L176 `tool_call_end{index:eventIndex}`。`CASES` ANT-009 把"不是 2 与 4"写死 |
| 4 | MF-S4 Gemini 数组切分 | **已关闭** | L189：「按**顶层 JSON 值的边界**流式切出对象——跟踪 `{}`/`[]` 深度与字符串/转义状态，深度归零即一个完整对象…**禁止用正则按逗号切分**」 |
| 5 | MF-S5 只认 `event:` | **已关闭** | L164：「`event:` 字段是**首选**分派依据；`event:` 缺失时 MUST 按 `data.type` 分派；两者冲突时以 `data.type` 为准并记 trace（对应 ANT-006）」 |
| 6 | MF-S6 `notice.params` 类型 | **已关闭** | L99：`params` 为 `map<string, string | string[]>`，「数组值就是规范，不得再序列化成逗号串」 |
| 7 | MF-E1 `INTERNAL` 的 stage | **已关闭** | `errors` L124 `INTERNAL | local`；L27 `local` 已进 §1 闭集「不属于以上任何阶段的本地缺陷」 |
| 8 | MF-E2 `trace_id` × SHA 判据 | **已关闭** | `errors` L68：「**非确定性字段**：MUST NOT 参与 conformance 字节比对（runner 比对前剔除）」；`CASES` L20 列出剔除清单（`trace_id`、`request_id`、`raw` 中 provider 生成的 id） |
| 9 | MF-E3 操作列 ↔ action 闭集 | **已关闭** | §3 六张表的操作列全部改成闭集名；§4 补齐 6 个 action（L157-162）；L164「新增 action 与新增 code 一样需要升版本」。机器复验：32 个 code 的 action 全部命中 §4（`used-but-undeclared: []`）。遗留一处新瑕疵见 S1 |
| 10 | MF-E4 `top_up` 无 URL 来源 | **已关闭** | `providers` L28 `billing_url?, status_url?`；`v1.sql` L64-65 两列；`errors` L151「URL 取自 `provider.billing_url`…**未配置时该 action MUST NOT 出现**，改用 `edit_provider`」。导出侧缺列见 D1 |
| 11 | MF-E5 脱敏管线顺序 | **已关闭** | `errors` L249-256：「**管线顺序固定，不得颠倒**」，四步写死（结构字段→模式→邮箱/路径→UTF-8 边界截断），并补「先截断后脱敏是缺陷」 |
| 12 | MF-E6 `IMPORT_*` 三个 code | **已关闭** | `errors` §3.6 L130-132 三个 code，stage 均为 `persist`，各有 2 条 action；`CASES` EXPORT-004 可用 |
| 13 | MF-P1 `temperature`/`top_p` 互斥 | **已关闭** | `providers` L111：「**canonical 里有 `temperature` 就发它、丢 `top_p`；没有 `temperature` 才发 `top_p`**。被丢弃的一方不产生事件」 |
| 14 | MF-P2 budget ↔ effort | **已关闭** | L123-129：`mode` 三级优先级 + 「**反向表**（budget → effort，上界含）：`< 2048` → low；2048~8191 → medium；≥ 8192 → high」。与 §3.4 正向量表互逆（1024/4096/16384 分别落回三档） |
| 15 | MF-P3 `first_byte`/`total` 无字段 | **未关闭** | `providers` L25-26、`v1.sql` L56-57 已加（含 CHECK 范围）；但 `export-v1.json` 的 `provider` 里**这两个属性根本不存在**（`grep` 零命中）。同 D1 |
| 16 | MF-P4 `cache.breakpoints` 的 notice | **已关闭** | L121：OpenAI/Responses/Gemini 两格均写「**不产生事件**…发 notice 会改变 `seq`」 |
| 17 | MF-R1 深度超限降级为 `text` | **已关闭** | `render-model` §5 L119：「降级为 `unknown{kind:"list"|"quote", raw:<原始标记>}`（**`text` 不是 Block 类型，不得用它**）」 |
| 18 | MF-R2 图片成块判定 | **已关闭** | §2.2 L74-76：inline 序列恰好一个 `image` 且"只有这一行源码"→ Block `image`；「**判定只看解析后的 inline 序列长度**，不看原始行内空格或缩进」。`CASES` RENDER-014 两种形态各一例 |
| 19 | MF-R3 引用式链接 | **已关闭** | §2.3 L80-82：v1 不解析，`[文字][label]` 原样为 `text`，`[label]: url` 归 `unknown{kind:"link_definition"}`；L82 明写理由就是 §4.5 前缀稳定性。`CASES` RENDER-015 |
| 20 | MF-V1 `message_part.truncated` | **未关闭** | 两处已对齐：`v1.sql` L262 `truncated INTEGER NOT NULL DEFAULT 0`；`ADR-003` §2.3 已改成「把**最后一个仍在增长的** `message_part` 标记 `truncated=true`…用户取消一律置 true」。但 `export-v1.json` 的 `message_part` 无此属性（`properties` 与 `required` 都没有）→ 往返丢字段。见 D1 |
| 21 | MF-V2 `provider` 缺 4 列 | **未关闭** | `v1.sql` L56-57、L64-65 四列齐全（`first_byte_timeout_ms` 5000–300000、`total_timeout_ms` 0 或 ≥30000）；`export-v1.json` 一个都没有。见 D1 |
| 22 | MF-X1 `content_omitted` | **已关闭** | `export-v1.json` `attachment` 现有 `content_omitted: boolean`；`notes` §3 补「`content_omitted=true` 时 `content_b64` 必须缺失」 |
| 23 | MF-X2 AAD 三份 / KDF 下限 | **已关闭** | `notes` §5：「**AAD 的唯一定义就是本节这一句**」；schema `ciphertext_b64.description` 指向它；`ADR-002` L30 改为「本 ADR 不复述该定义」；`iterations.minimum` 由 100000 提到 **600000** |
| 24 | MF-X3 DDL ↔ 导出四处不一致 | **未关闭（部分）** | ①②③④ 全部修好：`prompt`/`memory` 补了 `sync_version/rev/origin_device`，`mcp_server` 补了 `last_check_at/last_check_ok`，`attachment.local_path` 进了 `required`，四个实体的 `rev` 进了 `required`，从未被引用的 `$defs.sync_fields/timestamps` 已删除。但"**required 与 NOT NULL 全量对齐**"这条兜底被 draft.3 自己打破：`provider.first_byte_timeout_ms`、`provider.total_timeout_ms`、`message_part.truncated` 三个 NOT NULL 列不在 schema 里。见 D1 |
| 25 | MF-X4 容器格式未决 | **改偏了** | 好的部分：`notes` §6.1「**容器：只有单个 JSON 文件。** 不实现 zip」、§7 把 zip 列入"v1 不做"。偏的部分：§6.2 写「**大小上限：200 MB**，超过则拒绝导出…`export` 与 `import` 都 MUST 用流式 parser」，而 §4 的导入校验表仍写「文件大小 \| **≤2GB**（含内嵌附件） \| 拒绝，`IMPORT_TOO_LARGE`」。两种解读：**A** 导入上限也是 200 MB（§4 那行是漏改，残留旧值）；**B** 导出 200 MB、导入 2 GB（"导入宽容"）。A 与 B 对同一个 300 MB 文件给出相反结论（拒绝 vs 接受），且两端各选一个就分叉——正是这一条要消灭的那类歧义 |
| 26 | MF-C1 §0 判据 × `trace_id` | **已关闭** | `CASES` L20「比对前 MUST 剔除：`failed.error.trace_id`、`failed.error.request_id`、`failed.error.raw` 中由 provider 生成的 id」，并规定「剔除清单的改动需升版本」 |
| 27 | MF-C2 OAI-009 不可判定 | **已关闭** | L55：`meta.json` 标 `duplicate: true` 才去重，期望写死「**恰好 1 个 `delta`（`seq` 0），事件总数 = 2（含 `done`）**」，并补了反向约束「真实模型连发两个相同文本时（无此标记）MUST 原样发 2 个」。用例本身已可机器比对；但它依赖的判据在 `stream-events` 里仍不存在 → 见 D4 |
| 28 | MF-C3 CAN-002 的 truncated | **已关闭** | L120：「DB 中该 message `status='aborted'`，**最后一个 part `truncated=1`**（ADR-003 §2.3：用户取消一律置 1，不论是否已有 delta）」 |
| 29 | MF-C4 不变量无用例 | **已关闭** | 新增 FRAME-011（I6）、OAI-011（I8）、OAI-012（I11）、FRAME-012（I12），并加 §10.5 覆盖表 I1..I12 逐条映射；`stream-events` §6 的"每条不变量都有用例"现在成立 |

**计分：25 条已关闭 / 3 条未关闭（MF-P3、MF-V1、MF-V2）+ 1 条部分未关闭（MF-X3）/ 1 条改偏（MF-X4）。**

四条"未关闭"是同一个根因，不是四个独立问题：**draft.3 给 DDL 加了 5 列，`export-v1.json` 一列没跟**。修一次即可。

---

## 2. 新缺陷清单（只看"修出来的"）

### MUST-FIX

**D1 导出 schema 缺 draft.3 新增的 5 列，而 `additionalProperties: false` 两头都堵**

- 现象（机器比对，不是目测）：逐表比对 DDL 列与 `export-v1.json` 的 `properties`：

```
provider             absent=['first_byte_timeout_ms', 'total_timeout_ms', 'billing_url', 'status_url']
message_part         absent=['truncated']
provider_key         absent=['key_ref']        ← 刻意不导出，notes §8 已说明，正确
app_setting          present-but-optional=['updated_at']   ← 次要
```

  `grep -n "first_byte_timeout_ms\|total_timeout_ms\|billing_url\|status_url\|truncated" spec/schema/export-v1.json` 只命中 `message.truncated`（1178/1238 行），provider 与 `message_part` 侧零命中。
- 后果，三条都真实：
  1. `provider` 与 `message_part` 两个 `$defs` 都是 `additionalProperties: false` → 导出器把 DDL 的列原样写出，文件会被自己的 schema 拒绝（`notes` §4 要求"必须通过本 JSON Schema，失败即拒绝导入"）。
  2. 反过来不写 → `export → import → export` 丢 `first_byte_timeout_ms` / `total_timeout_ms` / `billing_url` / `status_url` / `message_part.truncated`，直接违反 `notes` §2 的"无损"定义。`first_byte_timeout_ms` 与 `total_timeout_ms` 在 DDL 里是 **NOT NULL**，导入端只能回落到默认 60000/0，于是长思考模型在导出导入之后超时行为变了。
  3. `billing_url` 丢失会静默改变**事件流**：`errors` §4 规定 `top_up` 在"未配置时 MUST NOT 出现"。同一份 provider 在两台设备上（一台有 URL、一台导入后没有）对同一个 402 产出不同的 `actions` 数组 → `events.jsonl` 字节不同。这不是显示差异，是判据差异。
- 改法：`$defs.provider` 加 4 个属性（`first_byte_timeout_ms`/`total_timeout_ms`: integer + DDL 同样的下限；`billing_url`/`status_url`: `["string","null"]`），`$defs.message_part` 加 `truncated: boolean`；把两个 NOT NULL 的 timeout 与 `truncated` 加进各自的 `required`（与 `message.truncated` 已有的做法一致）。`app_setting.updated_at` 顺手补进 `required`（DDL 是 NOT NULL 无默认）。

**D2 `notes` 里两个互相否定的导入上限（MF-X4 修偏的直接产物）**

- 原文两处：§4「文件大小 \| ≤2GB …\| 拒绝，`IMPORT_TOO_LARGE`」；§6.2「大小上限：**200 MB**…`export` 与 `import` 都 MUST 用流式 parser」。
- 两种解读：A 200 MB（§4 是漏改的旧值）；B 导出 200 MB / 导入 2 GB。对 300 MB 文件一端拒、一端收，`EXPORT-003` 那类"拒绝并给出错误路径"的断言也就无法双端对齐。
- 改法：把 §4 那行改成 200 MB 单值，并写明"导出与导入同一个常量"；§6.2 的句子保留为它的理由。

**D3 `settings_json` / `sampling_json` 的落盘形状与稀疏性没定义，§6.5 的"逐字段覆盖"因此有两种合法实现**

- 原文：`providers` §6.5.2「每层只覆盖自己出现的键；**显式 `null` 表示"取消下层设置，回到不发送"**，与"该键不存在"不同」；§2 的 canonical 示例则把所有键都写出来并置 `null`（`"top_k": null, "seed": null`）；`v1.sql` L218 只有一句注释「本会话的模型与采样覆盖」，`export-v1.json` 里两个字段是**不透明 `string`**。
- 两种解读，都合规，结果字节不同：**A（稀疏）** 只存用户显式改过的键 → 会话层 `{"temperature":null}` 只取消 temperature，Agent 层其它键照旧生效；**B（全量快照）** 存一份 canonical 形状的对象（这更自然，因为 canonical 本身就是全字段）→ 每个键都"出现"，其中 `null` 的键把 Agent 层**全部**取消。同一份用户设置，A 发 `top_p`，B 不发。
- 附带一个更硬的问题：blob 的顶层结构没定义——是 `{"sampling":{...},"reasoning":{...},"model_id":...}` 还是扁平？导出格式把它当不透明字符串，于是 Android 写出的导出文件在 Windows 上导入后，覆盖语义可能整段失效（`notes` §2 的"逐字段相同"在同一实现内成立，跨实现不成立）。
- 改法：在 §6.5 开头写死 blob 形状（建议 `{"model_id"?,"sampling":{...},"reasoning":{...}}`，只允许这三个键）与"**只存显式键，空对象 = 无覆盖，写入端 MUST NOT 落全量快照**"，并给一条 `request-golden` fixture（session 空对象 / session 置 `temperature:null` 两份）。

**D4 两条判据只活在用例里，`stream-events` 没有对应句子**

- (a) 事件顺序：`CASES` OAI-011 要求"一帧同时含 `content` 与两个 `tool_calls` 分片"时严格按 `delta → tool_call_delta[0] → tool_call_delta[1]` 发。`stream-events` §4.1 只有分行映射，I8 只说"一帧可产生 0..N 个事件"，**从未规定同一帧内的事件顺序**。一个先发 `tool_calls` 的实现完全符合 §4.1，却挂 OAI-011。
- (b) 去重口径：I11 是全局不变量「`delta` 的文本 MUST NOT 重发已发过的前缀（适配器须维护 `emitted_prefix_len` 做差集）」。按字面统一实施，OAI-009 的"无 `duplicate` 标记时真实重复 MUST 原样发 2 个"必然失败；反过来按 OAI-009 实施，I11 在 OpenAI 路径上形同不存在。上一轮的修法把 `duplicate: true` 放进 `meta.json`（用例可判定了），但 `stream-events` 侧一句去重判据都没补，I11 的措辞也没限定供应商形态。`stream-events` §1 自称"唯一的事实来源"，而这条事实只存在于 `CASES`。
- 改法：§4.1 表下加两句——「一帧内含 `content` 与 `tool_calls` 时，MUST 先发 `delta`，再按 `index` 升序发 `tool_call_delta`」；「**前缀去重只适用于累计型端点**（Gemini 数组/SSE、以及 `meta.duplicate` 显式标注的重发样本）；delta 型端点不得对相同文本做去重」。I11 改写为引用这两条，不再自成一体。

**D5 `CASES` §0 的 `asserts` 只定义了 `perf` 的形状，而新增的 FRAME-012 全部判据都在没定义的 `socket` 里**

- 原文 L21：「`asserts: {db: [...], socket: [...], perf: [{metric, op, value}]}`」——只有 `perf` 给了键形状，`db`/`socket` 是自由数组。FRAME-012 的唯一期望是「`asserts.socket` 断言活跃连接数为 0」，`count` 断言的键名、比较符、期望值全无定义 → 两端 runner 各写一套（`{"active":0}` vs `{"metric":"conns","op":"eq","value":0}`），"可判定"落空，`exit.json` 也无法跨端比对。CAN-003/CAN-004 同病（旧用例），但 FRAME-012 是这一轮新加的。
- 改法：三类断言统一成 `{metric, op, value}`（`op` 闭集 `eq|le|ge`，`metric` 用命名空间字符串如 `socket.active_connections`、`db.message.status`），FRAME-012 直接写成 `{"metric":"socket.active_connections","op":"eq","value":0}`。

### SHOULD-FIX

- **S1 `errors` §3.3 `CONTEXT_OVERFLOW` 的 action 列把 `reduce_context` 写了两遍**：「`reduce_context`<br>`reduce_context`<br>`switch_model`」。照表实现，`actions` 数组是 2 条还是 3 条不定（含重复项就进 `events.jsonl` 字节）。改法：重复那格换成 `view_raw` 或删掉。
- **S2 条件性 action 让老用例的期望依赖未固定的 fixture**：`top_up` / `view_service_status` 现在是"未配置即不出现"，而 ERR-002 断言「action 含 `top_up`」。`meta.json` 必须固定 `provider.billing_url`（以及 `status_url`），否则双端 fixture 不同 → `actions` 不同 → SHA 不同。改法：`CASES` §0 写明"凡断言条件性 action 的用例，`meta.json` 的 `config` 必须给出该 provider 的 URL"。
- **S3 §6.5 优先级链里的"模型元数据默认"没有数据来源**：`v1.sql` 的 `model` 表没有任何采样列（只有 `max_output`/`context_window`/价格）。这一层要么删掉，要么写明"仅 `max_output_tokens` 取 `model.max_output`"。
- **S4 `render-model` §2.2 会让最后一块在 Block `image` ⇄ `paragraph` 之间反复翻转**：输入 `![a](u)` → Block `image`；追加 ` x` → `paragraph{inlines:[image,text]}`。§4.5 允许（最后一块），但 §4 只给数学与 Mermaid 写了"原位切换、不重建视图"，图片的类型切换没有对应规则，实现可以整块重建而判据无言。
- **S5 两个新期望不是数字**：OAI-012「事件数 = 增量数」是循环表述（应为"3 个 `delta`，事件总数 4"）；FRAME-013「与非压缩版本逐字节相同」没指名配对用例（应写"与 GEM-001 的 `expected.jsonl` 相同"或自带 `expected.jsonl`）。
- **S6 四个文件自带的 §变更记录没跟 `CHANGELOG` 走**：`stream-events` §8、`providers` §9、`render-model` §9 仍只有 `1.0.0-draft` 一行；`errors` 有 draft 与 draft.3 两条。文件头的版本号是 draft.3，正文变更记录停在 draft。读者只能靠 `CHANGELOG.md` 才知道改了什么。
- **S7 `switch_model` 的 `require` 枚举缺 `json_schema`**：`providers` §6 的能力是六项，`errors` §4 只允许 `["vision"|"tools"|"long_context"]`。当 `CAPABILITY_UNSUPPORTED` 来自结构化输出时，这个 action 无法表达"只留支持 json_schema 的模型"，`params` 也进 `events.jsonl`。

---

## 3. 六个交叉点的结论（任务 B 的正面回答）

| # | 交叉点 | 结论 |
| --- | --- | --- |
| 1 | 16 MiB 单行/单帧 vs FRAME-009(10 MiB) / FRAME-010(20 MiB) | **互斥成立**。规则 10 与 11 同为 16 MiB，两档用例分居两侧；20 MiB 无论是一行还是多行拼成，都撞同一个上限、同一个 code。没有第二条规则给出不同阈值（`errors` L105 的标题也写 16 MiB） |
| 2 | `errors` 6 个新 action × 32 个 code | **自洽**。32 个 code 全部命中 §4（机器核对，`used-but-undeclared: []`），stage 全在闭集，无 code 缺 action。两处小瑕疵：`CONTEXT_OVERFLOW` 重复 action（S1）、`switch_model` 枚举缺项（S7）。另：`CHANGELOG` 写"21 个 action"，§4 实为 21 行，正确；`verify.py` 报 22 是因为把表头 `\| a \| 语义 \|` 当成了 action |
| 3 | §6.5 覆盖语义 × `settings_json`/`sampling_json` × canonical `null=未指定` | **null 语义本身不冲突**（"回到不发送" = "用 provider 默认"），**但落盘形状与稀疏性未定义 → 冲突**。见 D3 |
| 4 | §2.2 图片成块 × §4.5 前缀稳定性 | **不冲突**。§2.2 的判定只能改变"最后一块"的类型，而 §4.5 恰好豁免最后一块："先只有文本、后追加图片"（`见下图` → `见下图\n\n![a](u)`）产出 `[paragraph]` → `[paragraph, image]`，前缀不变。反向（`![a](u)` → `![a](u) x`）也只在最后一块上变化。残留的是 S4 的渲染层切换无规则，不是判据冲突 |
| 5 | DDL × 导出 schema（`message_part.truncated` + provider 四列） | **不一致**。5 列在 DDL 有、在 `export-v1.json` 完全没有（`required` 也没有，连 `properties` 都没有）。见 D1 |
| 6 | 新增 8 条用例的可判定性 | **6 条可判定，2 条有缺口**。可判定：FRAME-011（"只有 2 个 `delta`"）、FRAME-013（解压后逐字节等于非压缩版，配对用例待指名，S5）、OAI-011（写了明确事件序列，但 spec 没有这条顺序规则，D4a）、OAI-012（有 `expected.jsonl` 兜底，但正文"事件数 = 增量数"不是数，S5）、ANT-009（`eventIndex` 0/1，写死了"不是 2 与 4"）、RENDER-014/015（逐字段形态写死）。有缺口：**FRAME-012** 的全部判据落在 §0 未定义形状的 `socket` 断言里（D5）；**OAI-009** 数值写死了，但它规定的去重/保留规则 spec 里没有（D4b） |

---

## 4. 结论：**还需一轮**

不是"再全面评一遍"，是四件小事：

1. **D1**（导出 schema 补 5 列 + 3 个 `required`）——一次改完，同时关掉上一轮遗留的 MF-P3/MF-V1/MF-V2/MF-X3 四条。
2. **D2**（`notes` 2 GB / 200 MB 取一个）——改一行。
3. **D3**（`settings_json`/`sampling_json` 形状 + 稀疏语义 + 一条 fixture）——这是唯一需要"设计"的一条，但它决定 `request-golden` 判据是否成立。
4. **D4**（`stream-events` §4.1 补两句顺序与去重口径，改 I11 措辞）+ **D5**（断言形状）——都是加句子。

D1/D2/D3 全是"双端字节会不同"级别的判据缺陷，不能带着冻结；D4/D5 会让新加的两条用例（OAI-011、FRAME-012）沦为不可执行的裁判。合计改动约 10 处句子 + schema 5 个属性 + 3 个 `required`，返工量小，**但必须再走一次独立复验**——上一轮 29 条里有 4 条正是在"看起来改完了"的地方漏的口子，`verify.py` 全绿也没发现。

若只做 D1+D2 而不动 D3/D4/D5 就冻结：G1 会绿，但 `request-golden` 与两条新用例的红是随机的（取决于哪一端先写），那种绿比红更贵。

---

## 5. 对 `verify.py` 的评价

它做的事是真的：`v1.sql` 能 `executescript`、`foreign_key_check` 空、22 张表、CASES 计数 78 与表头一致、每组 ID 连续、五个文件版本号与 `PROJECT_STATE.md` 对得上、`INTERNAL` 不再有 `—`、`8 MiB` 与 `缺失→stop` 两个字符串已消失、禁语扫描。**作为"作者自检"，它抓到的都是它自己埋的已知回归点**，这一点它没有装样子。

但它在这轮最关键的地方是瞎的，而且瞎的方式很具体，不是"覆盖不全"这种空话：

**(1) 那条 `required` 对齐检查，只在列已经存在于 schema 里时才检查。** 原代码：

```python
if notnull and name in defs[df]['properties'] and name not in req:
```

`name in defs[df]['properties']` 这个前置条件，把"整列根本没进导出 schema"的情况直接跳过——而这正是 draft.3 的实际失败形态。证据：我在 `/tmp` 的副本里给 DDL 加一个 `zz_probe TEXT NOT NULL DEFAULT 'x'`（导出 schema 里没有），它照样 `OK export required aligned with NOT NULL` / `ALL PASS`；同一列一旦加进 `properties` 却不进 `required`，它立刻 `FAIL required missing: [('provider','zz_probe')]`。换句话说：**它只能证明"已写出的列都被声明为必填"，完全不能证明"DDL 的每一列都在导出里"**。而 `CHANGELOG` 里"required 与 NOT NULL 全量对齐（自动校验通过）"这句，就是靠这个检查得出的——它把"没检"报成了"通过"。

**(2) 没有反向覆盖检查。** 它只验 `required ⊆ DDL 列`（防幽灵列），不验 `DDL 列 ⊆ properties`。上面那 5 列缺失因此零告警。补一条 `for name in DDL 非主键列: assert name in properties`（`provider_key.key_ref` 白名单除外）就能挡住这一类，成本五行。

**(3) 它不读 `providers.md` 与 `render-model.md` 的正文。** 这两个文件只在第 4 节被取了一次版本号字符串。`providers` §6.5 的覆盖语义、§3.1 的优先级、`render-model` §2.2/§4.5 的判定，全都在检查范围之外——这一轮的两条最重要的新缺陷（D3、S4）正好落在这里，脚本结构上不可能发现。

**(4) 它对"语义矛盾"的检查是字符串匹配，只能抓自己写进去的那几个字面量。** `'8 MiB' in se`、`'缺失→`stop`' in se`：能抓这两个已知回归，抓不到"换一种说法写出的同类矛盾"，也抓不到 §2.3 与 §4.1 之间新增的第三处不一致。

**(5) action 闭集检查有一张手写的 `noise` 白名单（30 个词），并且把表头算作一个 action**（`| a | 语义 |` 命中正则 → 报 22，实际 21）。白名单是"为了让检查通过而列的豁免"，任何与 `index`/`data`/`topic` 同名的 action 都会被静默放过。要保留就把它改成显式排除表头，而不是排除 30 个词。

**(6) 它没有验 `export-v1.json` 本身是合法 JSON Schema，也没有验"每个 `$defs` 都 `additionalProperties:false`"或"每个 `$ref` 都能解析"。** `$defs.sync_fields`/`timestamps` 从未被引用那件事，上一轮是靠人眼看出来的；现在删了，但脚本没有任何机制防止下一次再引入孤儿 `$defs`。

**(7) 版本一致性只对 `PROJECT_STATE.md`，不对 `CHANGELOG.md`，也不对各文件自带的变更记录。** 所以 S6（正文变更记录停在 draft）与 `CASES` 独自跳到 1.1.0 这类漂移它看不见。

**(8) 它读不了用例的"可判定性"。** `FRAME-012` 的全部期望落在未定义形状的 `socket` 断言里，脚本只数了 ID 连不连续。这类缺陷只有人（或一个专门解析期望列的检查）能发现。

一句话：`verify.py` 现在值一个"回归哨"，不值一个"验收判据"。上面 (1)(2) 是五行补丁，(3)(8) 补不了，得靠评审。**把它的输出当作"CONFORMANCE 前提已成立"的证据是不成立的**——它连"这本 schema 描述了 DDL 的全部列"都没在查。
