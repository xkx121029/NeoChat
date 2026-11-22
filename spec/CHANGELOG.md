# spec/CHANGELOG

本文件只记录 `spec/` 的变更。代码变更走各自的 CHANGELOG 或发布说明。

格式：`<版本> | <日期> | <文件> | <变更摘要> | <受影响泳道> | <是否强制双端适配>`

| 版本 | 日期 | 文件 | 变更摘要 | 受影响泳道 | 强制适配 |
| --- | --- | --- | --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 全部 | 首版落盘：stream-events、errors、providers、render-model、schema/v1.sql、schema/export-v1.json、conformance/CASES | 全部 | 是（实现尚未开始） |
| 1.0.0-draft.2 | 2026-11-22 | stream-events | §5 新增悬挂流修复（启动时把 stale `streaming` 改为 aborted）；§7 明确“继续生成”是客户端续写 | 双端协议层 | 是 |
| 1.0.0-draft.2 | 2026-11-22 | render-model | §8 视觉容差定值（Android ≤2dp / Windows ≤2px）+ L4 行为等价；§2 mermaid 限定 flowchart/sequence、静态无动画 | 双端渲染 | 是 |

## 冻结流程（每个 spec 文件都适用）

1. Tech Lead 改文件并把版本升为 `x.y.z-draft`。
2. 独立评审（人类或独立 Agent，不得是作者）出具评审记录 → `docs/reviews/spec-review-<file>-<version>.md`。
3. 评审通过 → 版本改为 `x.y.z`（去掉 -draft）→ 在本表记一条 → 广播受影响泳道。
4. 双端适配 → 全量 conformance → 通过后才能进 Gate。

**执行者无权改 spec。无权在 spec 缺漏时自行填充细节。**
