# spec/CHANGELOG

本文件只记录 `spec/` 的变更。代码变更走各自的 CHANGELOG 或发布说明。

格式：`<版本> | <日期> | <文件> | <变更摘要> | <受影响泳道> | <是否强制双端适配>`

| 版本 | 日期 | 文件 | 变更摘要 | 受影响泳道 | 强制适配 |
| --- | --- | --- | --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 全部 | 首版落盘：stream-events、errors、providers、render-model、schema/v1.sql、schema/export-v1.json、conformance/CASES | 全部 | 是（实现尚未开始） |
| 1.0.0-draft.2 | 2026-11-22 | stream-events | §5 新增悬挂流修复（启动时把 stale `streaming` 改为 aborted）；§7 明确“继续生成”是客户端续写 | 双端协议层 | 是 |
| 1.0.0-draft.2 | 2026-11-22 | render-model | §8 视觉容差定值（Android ≤2dp / Windows ≤2px）+ L4 行为等价；§2 mermaid 限定 flowchart/sequence、静态无动画 | 双端渲染 | 是 |
| 1.0.0-draft.3 | 2026-11-22 | stream-events | 评审 must-fix：单行上限与单帧统一（16 MiB）；删掉 `finish_reason` 缺失→stop 的兜底；Anthropic `eventIndex` 定义死；Gemini 数组模式改为深度状态机；`event:` 缺失按 `data.type` 兜底；notice.params 支持数组；I8 允许 usage 合并；新增 `stream_interrupted`；Responses 截断并入终止标记 | 双端协议层 | 是 |
| 1.0.0-draft.3 | 2026-11-22 | providers | 评审 must-fix：temperature/top_p 优先级；reasoning mode 与 budget↔effort 反向表；补 `first_byte_timeout_ms`/`total_timeout_ms`/`billing_url`/`status_url`；cache.breakpoints 不产生事件；新增 §6.5 覆盖语义与 §6.6 并发上限；Gemini key 只走头 | 双端协议层 + 模型管理 | 是 |
| 1.0.0-draft.3 | 2026-11-22 | render-model | 评审 must-fix：深度超限降级为 `unknown`（`text` 不是 Block）；新增 §2.1 info string 语法、§2.2 图片成块判定、§2.3 引用式链接不解析；Inline 计数改 10；flags 增 has_list/has_footnote | 双端渲染 | 是 |
| 1.0.0-draft.3 | 2026-11-22 | schema/v1.sql + export-v1.json + notes | 评审 must-fix：`message_part.truncated`；provider 四个新列；导出补 sync 三件与 `last_check_*`；`content_omitted` 进 schema；AAD 唯一定义 + PBKDF2 下限 600000；容器定为单 JSON ≤200MB；required 与 NOT NULL 全量对齐（自动校验通过） | 存储层 + 导出 | 是 |
| **1.0.0** | 2026-11-22 | 全部 | **契约冻结**。四轮独立评审：29 + 5 + 4 + 5 条缺陷全部关闭；`tools/spec-lint/verify.py` 八项检查 + 八项自证全绿。冻结后任何变更必须升版本、广播、双端适配、跑全量 conformance | 全部 | 是 |
| 1.0.0-draft.6 | 2026-11-22 | providers | 第四轮 D3 残留：区分「协议必填字段的模型默认」（Anthropic `max_tokens` 取 `model.max_output`）与「用户层默认」；`ProviderConfig` 补 `cumulative_stream`；§6.6 不再复述默认值，指向 defaults 表 | 双端 | 是 |
| 1.1.3-draft | 2026-11-22 | conformance/CASES | 第四轮 D5 残留：新增「标识符来源闭集」（字面量/`last`/`$fixture.*`）；CAN-002 去掉哨兵 row_count；CAN-003 改夹具；EXPORT-003 改空 where 全表计数；perf 断言定为 N=100/p50/丢预热 | QA | 是 |
| 1.0.0-draft.5 | 2026-11-22 | providers §6.5 | 第三轮 D3 改偏修正：删掉不存在的"模型元数据默认"层；明文三态（缺键/显式 null/具体值）；落盘为扁平稀疏对象，允许顶层 null 但禁止嵌套与未知键 | 双端存储 + 状态层 | 是 |
| 1.0.0-draft.5 | 2026-11-22 | stream-events + v1.sql + export | 第三轮 N3：`cumulative_stream` 从 `extra_json` 私货改为 `provider` 真实列（含导出与 required） | 双端协议层 + 存储 | 是 |
| 1.0.0-draft.5 | 2026-11-22 | export-v1.json | 第三轮 N1：`total_timeout_ms` 用 `oneOf:[0, >=30000]` 表达 DDL 的 CHECK，消除"能过 schema 过不了 DDL" | 导入导出 | 是 |
| 1.1.2-draft | 2026-11-22 | conformance/CASES | 第三轮 D5：`asserts` 参与判定；`db.where` 定扁平对象语义；`socket` 收敛为 `stream_count_zero`+`within_ms`（含归还连接池）；6 条用例实例化真断言 | QA + 双端 | 是 |
| 1.0.0-draft.4 | 2026-11-22 | export-v1.json | 第二轮复验 D1：补 `first_byte_timeout_ms`/`total_timeout_ms`/`billing_url`/`status_url`/`message_part.truncated` 五列到 properties 与 required | 存储层 + 导出 | 是 |
| 1.0.0-draft.4 | 2026-11-22 | export notes | D2：导入表的大小上限从 2GB 改为 200MB，与 §6.2 统一 | 导入 | 是 |
| 1.0.0-draft.4 | 2026-11-22 | providers | D3：§6.5 写明 `settings_json`/`sampling_json` 是**稀疏对象**、恢复默认=删键、不存 null、顶层键闭集 | 双端存储 + 状态层 | 是 |
| 1.0.0-draft.4 | 2026-11-22 | stream-events | D4：§4.1 补"一帧多事件固定顺序"与"重复 chunk 去重判据"；I11 限定为累积型端点 | 双端协议层 | 是 |
| 1.0.0-draft.4 | 2026-11-22 | errors | SHOULD：`CONTEXT_OVERFLOW` 的 actions 去重 | 双端 | 是 |
| 1.1.1-draft | 2026-11-22 | conformance/CASES | D5：§0 的 `asserts` 形状与 `db.check`/`socket.expect`/`perf.op` 闭集定义完整，FRAME-012 变为可机器判定 | QA | 是 |
| 1.1.0-draft | 2026-11-22 | conformance/CASES | 评审 must-fix：比对剔除非确定性字段；OAI-009 可判定化；CAN-002 truncated 写死；新增 8 条用例（FRAME-011/012/013、OAI-011/012、ANT-009、RENDER-014/015）→ **78 条**；新增不变量覆盖表 | 双端协议层 + QA | 是 |

## 冻结流程（每个 spec 文件都适用）

1. Tech Lead 改文件并把版本升为 `x.y.z-draft`。
2. 独立评审（人类或独立 Agent，不得是作者）出具评审记录 → `docs/reviews/spec-review-<file>-<version>.md`。
3. 评审通过 → 版本改为 `x.y.z`（去掉 -draft）→ 在本表记一条 → 广播受影响泳道。
4. 双端适配 → 全量 conformance → 通过后才能进 Gate。

**执行者无权改 spec。无权在 spec 缺漏时自行填充细节。**
