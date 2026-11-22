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
| 1.1.0-draft | 2026-11-22 | conformance/CASES | 评审 must-fix：比对剔除非确定性字段；OAI-009 可判定化；CAN-002 truncated 写死；新增 8 条用例（FRAME-011/012/013、OAI-011/012、ANT-009、RENDER-014/015）→ **78 条**；新增不变量覆盖表 | 双端协议层 + QA | 是 |

## 冻结流程（每个 spec 文件都适用）

1. Tech Lead 改文件并把版本升为 `x.y.z-draft`。
2. 独立评审（人类或独立 Agent，不得是作者）出具评审记录 → `docs/reviews/spec-review-<file>-<version>.md`。
3. 评审通过 → 版本改为 `x.y.z`（去掉 -draft）→ 在本表记一条 → 广播受影响泳道。
4. 双端适配 → 全量 conformance → 通过后才能进 Gate。

**执行者无权改 spec。无权在 spec 缺漏时自行填充细节。**
