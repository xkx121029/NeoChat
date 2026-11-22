# NeoChat 契约独立评审 · 第三轮（收尾复验）

- 评审对象：`spec/providers.md` 1.0.0-draft.4、`spec/stream-events.md` 1.0.0-draft.4、`spec/errors.md` 1.0.0-draft.4、`spec/render-model.md` 1.0.0-draft.3（本轮未改）、`spec/schema/v1.sql`（1.0.0-draft，**本轮未改**）、`spec/schema/export-v1.json` + `export-v1.notes.md`、`spec/conformance/CASES.md` 1.1.1-draft（78 条）
- 待验清单：第二轮 `spec-review-2.md` §1 的 4 条未关闭（MF-P3/MF-V1/MF-V2/MF-X3）+ 1 条改偏（MF-X4 ≡ D2）+ §2 的 5 条新 MUST-FIX（D1..D5），共 9 条
- 评审者：独立评审（非作者），本机执行，仅读，未改任何 spec 字节
- 日期：2026-11-22
- 边界：只复验这 9 条 + 只查三处指定交叉点 + 只审 `verify.py` 的绕过面。已在前两轮确认无问题的部分不重做。

## 0. 只读证据

`git status --short` 无输出（评审前后工作区干净）。本轮文件指纹：

```
a1344d1d5dca37c690003c2ad8386a30236ad49ddf5f36dcb3d4042985f15ef4 *spec/providers.md
4d6f15c5312fef49912490597e57852616c06ced286ed163b797bfbf957a768c *spec/stream-events.md
01759ce8258fca844c4d6cf41d8c8bc4db1111d3c075cc906b28a5a073504e5b *spec/errors.md
283fba439779d370bf92f1c574fd11a3e2087fa5f71e9846a8d4bfc2e8e8e92f *spec/render-model.md
1bd377d0f661c4179f5366fa31ccc4ea36d84e991c9c967600c81df5cf6086c1 *spec/schema/export-v1.json
3b0d9c46b98602d14fd0ceafb3277e13b3a5a6af195b5eb0f7ab4b34370b15c8 *spec/schema/export-v1.notes.md
694cad0062b7c9ffd8fb140decbff91aae581dbe681da53b6dc43627d335a3ed *spec/schema/v1.sql
3280257ccfa58a76fb4faa6b8ec551e4ada2d8b585a3469a2902a81201018b27 *spec/conformance/CASES.md
149452ee7fa7b613572e21a1ffaace3625bd8c46faeb2264e1fbd316201375d9 *tools/spec-lint/verify.py
```

`v1.sql` 的哈希与第二轮完全相同（`694cad00…`）——本轮没有任何 DDL 变更，所以 D1 那五列只需比对"DDL 原本有什么"与"导出 schema 现在有什么"，不必怀疑两边同时漂移。

`python tools/spec-lint/verify.py` → `=== RESULT: ALL PASS ===`（17 条 OK）。这个 ALL PASS 与本文 §2 的 3 条新缺陷、§4 的绕过面并存，原因同前两轮：它没在查那几件事。

---

## 1. 九条复验表

口径沿用第二轮：**已关闭** = 有唯一正确读法且有原文；**未关闭** = 还差东西；**改偏了** = 修了但同一处留下两种解读。

| # | 编号 | 结论 | 依据（文件 + 原文/机器输出） |
| --- | --- | --- | --- |
| 1 | MF-P3 `first_byte`/`total` 无字段 | **已关闭** | `export-v1.json` L377-378 进了 `provider.required`，L495-500 两个属性都在：`first_byte_timeout_ms` `integer` 5000–300000、`total_timeout_ms` `integer` `minimum:0`。与 `v1.sql` L56-57 的 `NOT NULL DEFAULT 60000` / `DEFAULT 0` 对得上（范围的一处偏差见 N1） |
| 2 | MF-V1 `message_part.truncated` | **已关闭** | `export-v1.json` `$defs.message_part`：`truncated` 在 `properties`（`type: boolean`）且在 `required`；对应 `v1.sql` L262 `INTEGER NOT NULL DEFAULT 0 CHECK (truncated IN (0,1))` |
| 3 | MF-V2 `provider` 缺 4 列 | **已关闭** | 同上：`billing_url` L505、`status_url` L513 均为 `["string","null"]` + `maxLength:2048`；`$defs.provider` 是 `additionalProperties:false`，四条列现在都进得去 |
| 4 | MF-X3 DDL ↔ 导出全量对齐 | **未关闭（1 条残留）** | 前两轮的 ①②③④ 与五列都对了，但"required 与 NOT NULL 全量对齐"这句仍是假的：`v1.sql` L371-375 `app_setting.updated_at INTEGER NOT NULL`（无默认），`export-v1.json` L1761 `$defs.setting` 的 `required` 是 `["key","value_json"]`，没有 `updated_at`。机器复验：把 `app_setting→setting` 加进 `verify.py` 的 `MAP` 后立刻 `FAIL required 缺列: ['app_setting.updated_at']`（见 §4）。见 N2 |
| 5 | D1 导出缺 5 列 | **已关闭** | 逐列机器比对（DDL 非主键列 ⊆ 导出 `properties`，`provider_key.key_ref` 按白名单排除）：`missing_prop = []`。正对照也成立：手工删掉 `billing_url` 后 `FAIL 导出缺列: ['provider.billing_url -> provider']`。`verify.py` 新增的反向覆盖检查对这**一类**缺陷是真的有牙了 |
| 6 | D2 导出/导入大小口径 | **已关闭** | `notes` L36「≤200 MB（含内嵌附件），**与 §6.2 同一口径**」；L57「大小上限：**200 MB**…`export` 与 `import` 都 MUST 用流式 parser」。全仓 `grep "2GB"` 只剩 `CHANGELOG` 里那条历史记录（描述"从 2GB 改为 200MB"），无规定性残留 |
| 7 | D3 `settings_json` 稀疏形状 | **改偏了** | 新增的 L252-257 把稀疏与"不存 null"写死了，但 **L248 第 2 条仍原样保留**："**显式 `null` 表示"取消下层设置，回到不发送"**，与"该键不存在"不同"。同一节里两句话对着干，且 L256 又说导入时"剔除 null 键"——等于宣布 null 无意义。反例与两种读法见 §3.1 |
| 8 | D4 顺序 + 去重口径 | **已关闭** | `stream-events` L146「先发 `delta`，再按 `tool_calls[].index` 升序发 `tool_call_delta`」；L148-151 三段式去重判据（`meta.duplicate=true` / `cumulative_stream=true` 才做前缀差集，其余禁止按内容去重）；I11（L117）已限定为累积型端点。三句话互相对齐，OAI-009/OAI-011/OAI-012 现在都有正文依据。但 L150 里那句括号注释指了一个不存在的列 → N3 |
| 9 | D5 `asserts` 闭集 | **未关闭（部分关闭）** | 形状闭集是真写了（L21：`{db:[{check,args}], socket:[{expect,...}], perf:[{metric,op,value}]}`，三个闭集逐个列出），FRAME-012 的目标因此第一次有了名字可取。但仍有三处：①没有任何用例写出真正的断言对象（`grep` 全部 78 条，`no_active_connection`/`closed_after_done`/`row_count` 在本节之外零命中），全靠散文映射；②`socket.expect` 只给了名字没给语义，CAN-004「socket 已关闭」同时是 `closed_after_done`、`no_active_connection`、`cancelled_within_ms` 三个名字的候选；③`row_count`/`no_rows` 的 `where` 是自由值，语法未定义——CAN-002 的「3 个 delta 文本已落盘」和 CAN-003 的「无交错写同一 message（DB 断言）」都要靠它。见 §3.2 |

**计分：5 条已关闭（MF-P3、MF-V1、MF-V2、D1、D2）/ 2 条未关闭（MF-X3 残留 1 条、D5 部分）/ 1 条改偏（D3）/ 1 条关闭但引入新缺陷（D4 → N3）。**

第二轮那四条同根因的（MF-P3/V1/V2/X3）现在只剩 `setting.updated_at` 一条尾巴——一次改完 5 列那条判断是对的。

---

## 2. 新缺陷（修复引入的）

### N1 `total_timeout_ms` 的 CHECK 是"0 或 ≥30000"，导出 schema 只写了 `minimum: 0`

- 原文：`v1.sql` L57 `total_timeout_ms INTEGER NOT NULL DEFAULT 0 CHECK (total_timeout_ms = 0 OR total_timeout_ms >= 30000)`；`export-v1.json` L500-504 `{"type":"integer","minimum":0,"description":"0 = 不限"}`。
- 后果：`15000` 能过 schema 校验、过不了 DDL。`notes` §4 要求"必须先通过本 JSON Schema，然后一个事务内导入"，于是这类文件会在 INSERT 阶段被 SQLite 的约束错误打断，错误不是 `notes` §4 承诺的 JSON Pointer 路径，而是裸的约束文本；回滚没问题，但"哪条规则先拦、报什么"两端会各写一套。
- 对比同批其余四列：`first_byte_timeout_ms` 的 5000/300000 与 DDL 逐字相同（这一点任务里点名的怀疑不成立），`billing_url`/`status_url` 与 TEXT 可空一致（唯一收紧是 `maxLength:2048` 这个 DDL 没有的上限，属可接受的有意收紧），`message_part.truncated` 的 `boolean` ↔ `INTEGER CHECK IN(0,1)` 映射正确。**只有这一列漏了形状。**
- 改法一行：`{"anyOf":[{"const":0},{"type":"integer","minimum":30000}]}`（或 `"not":{"minimum":1,"maximum":29999}`）。

### N2 `setting.updated_at` 是 NOT NULL 却不在 `required`，而 `verify.py` 结构上看不见

- 原文见上表第 4 行。`verify.py` L34 把 `app_setting` 放进 `NOT_EXPORTED`，注释写"→ settings 集合，另行核对"，但脚本里没有任何一处做这个"另行核对"。所以"required 与 NOT NULL 全量对齐"在这张表上从未被验证过。
- 后果比 N1 轻，但性质相同：一份第三方/手工导出文件只要省掉 `updated_at`，就能过 schema、然后撞 NOT NULL。手工导出不该被信任，这正是 §4 那张表存在的理由。
- 改法：`$defs.setting.required` 加 `updated_at`；`verify.py` 把 `app_setting→setting` 并进 `MAP`（`updated_at` 进 required 后即可常驻检查）。

### N3 `stream-events` 把 `cumulative_stream` 指向 `provider.extra_json`，而 provider 表没有这一列

- 原文：`stream-events` L150「或该 provider 被标记为 `cumulative_stream=true`（**存 `provider.extra_json`**）」。
- 机器复验：`grep -rn "extra_json" spec/` 只命中两处——`v1.sql` L122 与 `export-v1.json` L725，**都在 `model` 表/`model` $def 里**。`provider` 的 DDL 列是 `extra_headers_json`/`extra_query_json`/`extra_body_json`（L60-62），没有 `extra_json`；导出 `$defs.provider` 也没有。
- 后果三条：①实现无从落盘，两端会各选一个家（一端加列、一端塞 `app_setting`），`M0-QE-001` 的 schema-diff 立刻红；②这个标记不导出，于是"导出→导入"之后同一 provider 的去重行为会静默改变——正好违反 `notes` §2 的无损定义；③它决定 I11 是否生效，是判据级字段，不是注释级。
- 改法：二选一，一句话——在 `provider` 加 `extra_json TEXT`（连带导出/required），或改成"存 `app_setting` 键 `provider.<id>.cumulative_stream`"；顺带检查 `providers.md` §1 的 `ProviderConfig` 里是否也要列这一个布尔。

---

## 3. 三处指定交叉点

### 3.1 `providers` §6.5「稀疏、不存 null」× canonical §2「null = 未指定」

**canonical 那半是对的，坏的在同一节内部。** canonical §2 规则 3（L92）说 `null` = 未指定、与显式 0 要区分；§6.5 L255 说"canonical 里出现的 `null` 只可能来自所有层都没设"——这两句自洽，稀疏落盘不与之冲突（用户设 `0.0` 存 `0.0`，没有一层设就整键缺失→ canonical null）。

矛盾在 §6.5 L248 与 L252-257 之间：L248 仍在定义"显式 `null` = 取消下层、回到不发送，与键不存在不同"，而 L252 说"**绝不含 `null` 值"、L254 说"恢复默认 = 删除该键，不是存 `null`"、L256 说导入时"剔除 null 键"。四句里有一句是死条文，读者得自己选。

**反例（任务指定：用户把 temperature 从 0.7 改成默认）**：场景是 `agent_profile.sampling_json = {"temperature":0.9}`，会话层用户设 `temperature=0.7`，随后点"恢复默认"。

- 读法 A（按 L254 删键）：会话层该键消失 → 落回 Agent 层 → 线上请求带 `temperature: 0.9`。
- 读法 B（按 L248 存 `null`）：会话层显式取消 → 线上请求**不带** `temperature`，由 provider 兜底。

同一串用户操作，两个请求体、两份 `request-golden` SHA 都对。更糟的是读法 B 在新规则下根本写不进库（L252 禁止 null），所以"恢复默认"实际含义是**"回到下层继承"而不是"回到 provider 默认"**——而 UI 上这两个说法用户分不出来。这不是措辞问题，是 `request-golden` 判据成不成立的问题。

另有一层是第二轮 S3 的延续，现在被这条放大：L247 的优先级链里"模型元数据默认"这一层**没有数据来源**（`v1.sql` 的 `model` 表只有 `context_window`/`max_output`/`thinking_budget_max` 与价格，没有任何采样列）。删键之后要落的那一层不存在，于是"恢复默认"在单层场景下也无定义。

一条改法（需作者拍一次，改 1 段）：删掉 L248 的 null 语义，或明确二选一并写进 L248——建议"v1 不提供跨层强制不发送：删键 = 回到下层；要强制不发，删掉下层的同名键"；同时把 L247 的"模型元数据默认"删掉或写明"仅 `max_output_tokens` 取 `model.max_output`"。配两份 `request-golden` fixture（Agent 0.9 + 会话空 / Agent 0.9 + 会话 0.7）即可把这条钉死。

### 3.2 `CASES` §0 的 `asserts` 闭集 × FRAME-012 / CAN-002 / CAN-004

**形状不再打架，名字和语义还在打架。**

- FRAME-012（L40）能唯一落位：`{expect:"no_active_connection"}`——"活跃连接数为 0"与这个名字是一一对应的。这一条实质关闭了。
- CAN-004（L122）落不了位：原文"socket 已关闭（OkHttp EventListener / SocketsHttpHandler 计数断言）"，而 §0 给的三个名字都能声称覆盖它——`closed_after_done`（取消后确有 `done{aborted_user}`）、`no_active_connection`、`cancelled_within_ms`（取消后 N ms 内关闭）。三种写法都"合规"，`exit.json.asserts` 里会出现三种不同的 `id` 语义。更硬的冲突是与 `stream-events` I12 的措辞：I12 允许"关闭 socket **或归还连接到池**"，`providers` §7 还要求同 provider 复用连接池；如果池里空闲的 socket 算 "active"，一个**完全合规**的实现会挂 FRAME-012。`no_active_connection` 到底是"无 open socket"还是"无 in-flight 流"，§0 一个字都没说。
- CAN-002（L120）第二半落不了位：`status='aborted'` 与 `part.truncated=1` 有名字（`message_status`、`message_part_truncated`），但「3 个 delta 文本已落盘」只能靠 `row_count{table:"message_part", where:…, count:3}`，而 `where` 是自由值、没有语法。CAN-003 的「无交错写同一 message（DB 断言）」同病。两端各自发明一套谓词语法，`meta.json` 是**共享 fixture**，同一条断言在一端判 ok、另一端解析失败——这已经是跨端分歧，不只是"断言偏弱"。
- 另有一个结构性放大器：§0 的判据（L19）只比对 `events.jsonl` 的 SHA-256、`expected.jsonl`、`expect_error`，**`exit.json.asserts` 不参与比对**。加上"缺 `asserts` 视为不检查"（L21），一条表达不出来的断言最省事的落地方式就是删掉它——两个 runner 都绿，谁也不知道少验了什么。

改法（两行 + 三条用例各补一个断言对象）：`where` 定成闭集形状，建议 `{"column":"<ddl 列名>","op":"eq|ne|in|not_null","value":…}`；`socket.expect` 名称下加一句语义（建议明写 `no_active_connection` = 无处于 in-flight 的响应体读取，**不含**池中空闲连接，从而与 I12/§7 不冲突）；FRAME-012 写成 `{"expect":"no_active_connection"}`、CAN-004 逐条点名、CAN-002 补 `row_count`。

### 3.3 导出新增 5 列 × `v1.sql` 的 DEFAULT / CHECK

逐列机器比对（`CHECK BETWEEN` / `IN (…)` 与 JSON 的 `minimum`/`maximum`/`enum` 全表扫）：

| 列 | DDL | 导出 schema | 结论 |
| --- | --- | --- | --- |
| `provider.first_byte_timeout_ms` | `NOT NULL DEFAULT 60000 CHECK BETWEEN 5000 AND 300000` | `integer` 5000–300000，required | **一致** |
| `provider.total_timeout_ms` | `NOT NULL DEFAULT 0 CHECK (=0 OR ≥30000)` | `integer` `minimum:0` | **不一致**（允许 1–29999）→ N1 |
| `provider.billing_url` | `TEXT` 可空 | `["string","null"]` maxLength 2048 | 一致（多一个 DDL 没有的长度上限，属有意收紧） |
| `provider.status_url` | `TEXT` 可空 | 同上 | 一致 |
| `message_part.truncated` | `NOT NULL DEFAULT 0 CHECK IN (0,1)` | `boolean`，required | 一致（0/1 ↔ boolean 映射正确） |

顺带确认同族的一致项没被这次改动碰坏：`connect_timeout_ms`、`read_idle_timeout_ms`、`max_retries`、`concurrency_limit`、`provider_key.health_score` 的 `BETWEEN` 全部与 `minimum`/`maximum` 逐字相同；所有 `IN (…)` 的文本闭集（`protocol`/`proxy_mode`/`role`/`kind`/`transport`/`danger_level`/`memory_scope`/`max_tokens_param`/`message.status`/`tool_call.status`）都与 `enum` 对上，`message_attachment.role_hint` 的 `enum` 正确带了 `null`。

---

## 4. `tools/spec-lint/verify.py`：还能被绕过，而且新自证是空的

**先回答任务里那一问：DDL 列写进导出 `properties` 但类型错了（INTEGER 写成 string），它不会报。** 机器证据（在 `~/.capy/work/spec-review-3/probe` 的副本上做，未动 `E:\NeoChat`）：

```
探针 A：$defs.provider.properties.first_byte_timeout_ms = {"type":"string"}
        （DDL 仍是 INTEGER NOT NULL DEFAULT 60000 CHECK 5000~300000）
→ === RESULT: ALL PASS ===

探针 C：first_byte_timeout_ms 的 minimum 由 5000 改成 1
探针 D：可空列 provider.org_id 改成 {"type":"string"}（不允许 null）
→ === RESULT: ALL PASS ===
```

这三个探针覆盖的正是这次 D1 修复的性能面：脚本只验"名字在不在"，不验"形状对不对"。所以它现在能证明的是一句话——**"DDL 的每一列都在导出里出现过"**——仅此而已。上一轮它只能证明"写出来的列都必填"，这一步是真进步（正对照见 §1 第 5 行）；但"这本 schema 能描述 DDL 的每一列"仍然不成立。

**新加的"反向覆盖自证"是空转的，这一点更要紧。** L136-140 的实现是：

```python
probe = [c[1] for c in con3.execute('PRAGMA table_info(provider)')
         if c[1] == 'selftest_col' and c[1] not in props]
```

`ALTER TABLE … ADD COLUMN selftest_col` 在同一条语句之前必然成功，而 `selftest_col` 永远不可能出现在真实 `props` 里，所以 `probe` **恒为 `['selftest_col']`**，这条 `OK` 与检查 1 是否还活着完全无关。机器证据：把检查 1 那一行改成 `pass`（模拟它被删掉或改坏），脚本仍然打印

```
OK   反向覆盖自证：注入一列后检查器能识别
=== RESULT: ALL PASS ===
```

它没有调用被验的那个检查，只是把同样的正则原地重写了一遍。**自证要有效，必须调用出货代码**：把检查 1 抽成 `def check_reverse_coverage(con, defs) -> list[str]`，正式跑一次，再在 `con3` 上跑同一个函数并断言返回非空。否则它就退化成"证明 sqlite3 的 ALTER TABLE 能用"。

其余的绕过面（按可被利用程度排序，都是本轮实测或逐行读出来的）：

- 反向覆盖只走 `MAP` 里那 18 张表。`tombstone` 既不在 `MAP` 也不在 `NOT_EXPORTED`（导出 schema 里却有 `$defs.tombstone`），完全没人核对；`app_setting` 被移出后"另行核对"四个字是句空话（→ N2 就是从这里漏的）。
- 一条走到尽头会静默放行的路径：`required` 里出现 `properties` 没有的名字时，检查 2 因 `name not in props` 跳过、检查 3 因该列在 DDL 里而放过——`required ⊄ properties` 这种直接违反 JSON Schema 的形态无人拦。
- 语义检查仍是字面量匹配：`'8 MiB' not in SE`、`'缺失→`stop`' not in SE`、`'2GB' not in NOTES.replace('≤200 MB','')`。最后那句先把正确写法从文本里抠掉再找错写法，本身就是为"让检查通过"留的口子——`2048 MB`、`2 GB`、`"2GB" 以外任何写法都能穿过。
- 不读 `providers.md` 与 `render-model.md` 正文（只取一次版本号字符串），不校验 `export-v1.json` 是否是合法 JSON Schema、有没有孤儿 `$defs`、`$ref` 能不能解析，不核 `CASES` 的断言是否落在 §0 闭集里。本轮三条新缺陷（N1/N3/D3 的矛盾）全部落在这些盲区里，脚本结构上不可能发现。

**建议补的 3 条检查（按价值排序）：**

1. **类型对齐**。把 `PRAGMA table_info` 的声明类型映射成 JSON 类型：`TEXT`→`string`、`INTEGER`→`integer`、`REAL`→`number`；`INTEGER` + `CHECK … IN (0,1)` →`boolean`；`$ref`/`oneOf` 先解析到叶子再比。类型错的列每一条合法数据都会被自己的 schema 拒掉，是"导入永远失败"级别，和 D1 同族。探针 A 立即能变成红灯。
2. **可空性对齐（双向）+ 把 `app_setting` 并进 `MAP`**。现有检查只管 `NOT NULL ⊆ required`；缺的是"DDL 可空列必须允许 `null`"（探针 D：`org_id` 写成 string-only 会让所有 NULL 行导出失败）和"`NOT NULL` 列不得允许 null"。顺带补上 N2 与 `tombstone` 的覆盖缺口，五行。
3. **CHECK 范围对齐**。把 `BETWEEN a AND b` 映射成 `minimum`/`maximum`，`IN (…)` 映射成 `enum`，并强制要求"DDL 有 CHECK、导出无对应约束"时至少显式写在白名单里（`total_timeout_ms` 的 `=0 OR ≥N` 形态可以要求 `anyOf` 或 `not`）。N1 就是这条的产物。

---

## 5. 最终结论：**还需一轮**（但只剩 4 处一行级改动）

九条里 5 条真关掉了，D1 是这轮最实的一次修复。剩下四条各有明确落点，没有一条需要重新设计：

1. **D3（MUST）**：删掉 `providers` L248 的 null 语义，或按 §3.1 的二选一改写；L247 的"模型元数据默认"一并处理。**这条需要作者拍一次方向**（"删键=回到下层" vs "v1 不支持跨层强制不发"），因为两种选择给出不同的请求字节。配两份 `request-golden` fixture。
2. **D5 残余（MUST）**：定 `where` 的闭集谓词形状、给 `socket.expect` 写一句语义（明确池中空闲连接不算 active，否则与 I12/§7 冲突），三条用例各补一个真正的断言对象。
3. **N3（MUST）**：`provider.extra_json` 换成真实落点，一句话。
4. **N2 + N1（SHOULD）**：`setting.updated_at` 进 `required`；`total_timeout_ms` 用 `anyOf` 表达 "0 或 ≥30000"。

冻结前的最后一条动作：**这四处改完后，用上面 §4 的三条新检查武装 `verify.py`，让它在当前 spec 上就先红一次（N1 的 `total_timeout_ms` 与 N2 的 `setting.updated_at` 就是现成的回归样本，改完后转绿），然后再做一次只看 `providers` §6.5、`CASES` §0 与 `export-v1.json` 两列的定点复验**——不需要再走一遍全量轮。若把 N1/N2 拖到冻结后，它们会在第一次真实导入的失败现场才暴露，而那时的错误信息是 SQLite 的约束文本，不是 `notes` §4 承诺的 JSON Pointer。

第二轮的判断在这轮得到第三次印证：真正的漏口都长在"看起来改完了"的那句话旁边——`§6.5` 的 sparse 段落紧挨着没删的 null 条文，`D4` 的正文句子顺手写了一个不存在的列，`D1` 的 required 补齐漏了它自己承诺的那张表。

---

## 附：本轮不在范围内、但仍未处理的事项（仅记录状态）

第二轮 SHOULD 里这些**在现文中仍保持原样**，未随 draft.4 一起处理：S2（`CASES` §0 未要求断言条件性 action 的用例在 `meta.json` 固定 `billing_url`/`status_url`，`grep` 零命中）、S3（§6.5 优先级链里的"模型元数据默认"仍无数据来源，见 §3.1）、S4（`render-model` 本轮未改，§2.2 图片成块的类型翻转仍无切换规则）、S5（OAI-012 仍写"事件数 = 增量数"）、S6（`stream-events` §8、`providers` §9、`render-model` §9 的变更记录仍停在 `1.0.0-draft`；`errors` §8 停在 `draft.3`，draft.4 未记）、S7（`switch_model` 的 `require` 闭集仍缺 `json_schema`）。它们不阻塞冻结，但 S6 会让"这一版改了什么"只能靠 `CHANGELOG.md` 回溯。
