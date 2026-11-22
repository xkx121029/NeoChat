# NeoChat 契约独立评审 · 第四轮（定点复验）

- 评审对象：`spec/providers.md` 1.0.0-draft.5、`spec/stream-events.md` 1.0.0-draft.5、`spec/conformance/CASES.md` 1.1.2-draft、`spec/schema/v1.sql`、`spec/schema/export-v1.json`、`tools/spec-lint/verify.py` v2
- 范围：第三轮 `spec-review-3.md` §5 点名的 4 条（D3 / D5 / N3 / N1）+ 第 5 问（verify.py 剩余绕过面）。前几轮已关闭的部分不重做。
- 评审者：独立评审（非作者），本机执行，仅读
- 日期：2026-11-22
- 只读证据：评审前后 `git status --short` 均无输出；六份文件指纹未变（`providers.md` 3576898b…、`stream-events.md` bbb3a74c…、`CASES.md` ca08ec45…、`v1.sql` f921e007…、`export-v1.json` 490f0290…、`verify.py` a5d86399…）。所有变异实验在 `~/.capy/work/spec-review-4/probe` 的副本上做，`E:\NeoChat` 零字节改动。

## 1. 四条结论

| 编号 | 结论 | 依据 |
| --- | --- | --- |
| **D3** | **未关闭（1 处，跨节）** | 原病已真修：`providers.md` L249-252 的三态（缺键→回退 / `null`→停止回退且不发送 / 具体值→采用）与 L256-260 的落盘形状（扁平稀疏、恢复默认=删键、显式不发=存 null、允许顶层 null、禁嵌套与未知键）现在互推一致，L248 也明写「没有『模型元数据默认』这一层」，第三轮 §3.1 的 null-vs-sparse 矛盾消失。指定反例现唯一确定：`agent_profile.sampling_json={"temperature":0.9}` + 会话 `0.7` + 点「恢复默认」→ L258 删会话键 → 回退到 Agent 层 → 请求体 `temperature: 0.9`，无第二读法。**但 L248 那句否认撞上了 §3.1 L113**：anthropic 列写「`max_tokens`（**必填**，缺省取模型默认）」，而 `model.max_output` 是真实列（`v1.sql` L127），也是 `max_output_tokens`（canonical `sampling` 的键，L75）的唯一默认来源。于是「会话与 Agent 都未设 `max_output_tokens`」时：按 §6.5 不发送 → anthropic 400；按 §3.1 发 `model.max_output` → 200。同一个 `ChatRequest` 两个请求体，属于 D3 同族缺陷换了个字段。改法一行：L248 改成「除 `max_output_tokens` 取 `model.max_output` 外，无模型元数据默认层」 |
| **D5** | **未关闭（4 处一行级；3 项已真正关闭）** | 已关闭：①`CASES.md` L21 现明写「任何一条断言失败 → 该用例失败」，并要求 `exit.json.asserts` 逐条输出 `{id, ok, actual}`、缺失条目判失败——第三轮 §3.2 那条「删掉断言最省事」的结构性放大器被堵住；②`socket` 形状收敛为唯一值 `{expect:"stream_count_zero", within_ms}`，且 L24 明确「归零的含义包含归还到连接池——池中空闲连接不算 active（否则与 providers §7 冲突）」，FRAME-012、CAN-004 因此可以唯一落位，一个合规实现（归还池、服务端仍占连接）不会被误判；③`where` 有了闭集语法（扁平对象、列名→值、全 AND、不支持嵌套与运算符），机器可解析。仍缺四处，每处一句：**(a)** CAN-002 的 `row_count.args.where` 用了 `{"message_id":"last"}`，而 `"last"` 这个哨兵只在 `message_status.args` 的词表里定义过（L22），`where` 的语法没有——按字面读是「message_id 等于字符串 `last`」，命中 0 行，`count:1` 永远不成立，**合规实现必挂**；按哨兵读则两端各写一套。**(b)** CAN-003 的 `{"conversation_id":"c0"}` 里 `c0` 全仓零定义（`grep -rn '"c0"' spec/` 只命中该行本身，`spec/conformance/samples/` 尚不存在），真实实现写的是 UUID，断言不可判定。**(c)** EXPORT-003 的 `no_rows{conversation, origin_device:"import"}` 是空断言：`v1.sql` L16 定义 `origin_device` 为写入设备 id，全仓无任何一处要求导入写 `"import"`，于是该断言对任何实现恒过，捕捉不到「导入残留」。**(d)** RENDER-012 的 `perf.metric:"append-10"` 是自由串：没有测量定义（测哪段、是否含 IPC/落盘）、没有采样与容差策略，`op:"lte"` 卡 2 ms 单样本，两端测的不是一件事，合规实现在忙机器上也会红 |
| **N3** | **已关闭** | `provider.cumulative_stream` 是真实列：`v1.sql` L64 `cumulative_stream INTEGER NOT NULL DEFAULT 0 CHECK (cumulative_stream IN (0,1))`；导出 `$defs.provider.properties.cumulative_stream` 为 `{"type":"boolean"}` 且在 `required`（export-v1.json L379、L529），与 `NOT NULL` 及 `pass_through` 同族映射一致；`stream-events.md` §4.1 L150 现写「该 provider 的 `provider.cumulative_stream=1` 时（真实列，见 `v1.sql`；不是 `extra_json` 里的私货）」，I11（L117）同步指向同一列。三处一致。残留（SHOULD，不阻塞）：`providers.md` §1 的 `ProviderConfig` 未列这个字段，与 `pass_through` 并列更合形状——适配器契约上看不见它，只能从持久化的 provider 行里读 |
| **N1** | **已关闭** | `export-v1.json` 的 `provider.total_timeout_ms` 现为 `{"oneOf":[{"const":0},{"type":"integer","minimum":30000}]}`，与 `v1.sql` L57 `CHECK (total_timeout_ms = 0 OR total_timeout_ms >= 30000)` 等价。逐值机器比对（同一实例分别走 JSON Schema 与 `INSERT`）：`0` 双过、`15000` 双拒、`29999` 双拒、`30000` 双过、`-1` 双拒——`15000` 现在两边都进不去。方向性也成立：SQLite 数字亲和会放过 `30000.5` 而 schema 拒（DDL 在这一点更松），但 `notes` §4 规定「先过 JSON Schema 再导入」，因此不存在「过 schema 却被 DDL 拒」的文件，契约的判据方向无缺口 |

## 2. 第 5 问：还剩哪种单一改动能让 spec 与 schema/DDL 不一致而六项全过

两条，按现实风险排序，均在 `probe` 副本上实测（`verify.py` 打印 `=== RESULT: ALL PASS ===`）：

1. **文本枚举漂移（风险最高）。** `check_ranges` 只解析两种 CHECK 形态——`BETWEEN a AND b` 与 `= 0 OR x >= n`；`CHECK (x IN (...))` 与导出 `enum` 从来不比。实测探针 P1：把 `provider.protocol` 的 CHECK 加上 `'bedrock'`，导出 `enum` 保持六个值不动 → 六项全绿。反向同理（从 export `enum` 删一个值，DDL 不动）。现实性最高，因为新增协议、新增 `kind`/`status`、改 `stop_reason` 闭集正是「顺手一行」的改动，而这类列有十几条。
2. **DDL DEFAULT 与正文数字漂移（次之）。** 六项检查完全不读 `DEFAULT`，也只从 `providers.md` 取一次版本号字符串、不读任何阈值正文。实测探针 P2：`concurrency_limit` 的 `DEFAULT 4` 改成 `8`，`providers.md` §6.6 L266 仍写「默认 4」→ 六项全绿。同一手法适用于 `first_byte_timeout_ms`（60000）、`read_idle_timeout_ms`（180000）、`max_retries`（2）、`net.max_concurrent_streams` 的 4/16。

（`required ⊄ properties` 那条结构性盲区仍在——`check_missing_required` 因 `name not in props` 跳过、`check_phantom_required` 因列存在于 DDL 而放过——但它产生的是 schema 自身不可满足，不算「spec 与 DDL 不一致」，且前两类更常发生，故列在两条之外。）

## 3. 最终裁决

**不能冻结**：D3 的跨节一句（`max_output_tokens` / anthropic `max_tokens`）与 D5 的四处悬空断言（`where` 里的 `"last"`、`"c0"`、`origin_device:"import"`、`perf.metric`）都是「改一句话或删一个断言」的量级，但这五处里有三处会让一个合规实现在 conformance 里直接挂（CAN-002、CAN-003、RENDER-012），冻结即意味着 G1/G3 第一次跑就会出双端都解释不了的红色。

改完这五处后，只需再验两条命令即可收尾：`python tools/spec-lint/verify.py`（`ALL PASS`），以及把 `provider.protocol` 的枚举与 `concurrency_limit` 的 DEFAULT 各补进 v2 的两条新检查（op 见 §2 探针），让它们先红后绿。
