# export-v1.notes.md — 无损往返的判据与已知取舍

配套 `spec/schema/export-v1.json`。

## 1. 为什么字段名用 snake_case

导出格式刻意与 `v1.sql` 的列名保持一字不差。常规做法是导出的 JSON 用 camelCase 更好看，但那会引入一层映射，而映射层正是往返丢字段的高发区（一票否决项）。字段名的可读性可以通过字段说明弥补，丢一个字段不行。

## 2. "无损"的精确定义

往返无损 = 对任意会话集合，执行 `export → import → export` 后，两次导出文件的 `data` 部分**逐字段相同**（键排序后字符串比较，忽略 `exported_at` 与 `integrity`）。

必须保住的东西：

1. 全部 `id`（不做重映射，用 UUIDv7 保证跨库不冲突）。
2. 消息树的 `parent_id` 与 `sibling_index`（分支结构）。
3. `deleted_at`（回收站内容，否则不无损）。
4. 字段类型（`0` 与 `0.0`、`null` 与缺省、字符串 `"0.7"` 与数字 `0.7`）。
5. 顺序（消息、part、用量、标签关联的数组顺序与导出前查询顺序一致，按主键升序排列）。
6. 附件的 `sha256` 与引用关系（字节可选，元数据必带）。
7. 用量记录与估算标记（`is_estimate`、`error_bound_pct`）——否则历史成本不可复核。
8. 思考内容（`kind='reasoning'`），除非显式 `include_reasoning=false`（此时该次往返不算无损，导出报告必须标注）。

## 3. 附件策略

- 默认 `include_attachments='none'`：只导出元数据。导入后附件缺失，UI 标注"附件未包含在此次导出中"，**不静默显示空图**。
- `inline`：字节以 base64 内嵌。单文件上限 `size_bytes ≤ 20MB`，超限的附件降级为元数据并在 `attachment` 上标 `content_omitted=true`（该字段仅在 inline 模式出现，schema 允许 `content_b64` 缺失）。
- 导入时按 `sha256` 去重：已存在同哈希附件则只建立引用，不重复落盘。

## 4. 导入校验（防攻击）

导入是攻击面（风险表"恶意导入 JSON"）。规则：

| 检查 | 上限/规则 | 失败行为 |
| --- | --- | --- |
| 文件大小 | ≤2GB（含内嵌附件） | 拒绝，`IMPORT_TOO_LARGE` |
| JSON 嵌套深度 | ≤32 | 拒绝，`IMPORT_MALFORMED` |
| 集合元素数 | 单集合 ≤500,000 | 拒绝 |
| schema 校验 | 必须通过本 JSON Schema | 拒绝，给出首个错误路径 |
| `format_version` | 必须为 1 | `IMPORT_UNSUPPORTED_VERSION`（保留文件，不尝试部分导入） |
| 引用完整性 | 所有 `parent_id`/`conversation_id`/`*_id` 必须能在同一文件内解析 | 拒绝（不做"尽力导入"，那会造出孤儿数据） |
| `local_path` | 必须是相对路径，不得含 `..` 或盘符 | 拒绝（路径穿越） |
| 字符串长度 | 单字段 ≤20MB | 拒绝 |
| 事务 | 全部导入在**一个事务**内，失败整体回滚 | — |

## 5. 密钥导出（与 ADR-002 对应）

- `include_secrets=true` 时必须有 `secrets` 块，且用户提供口令（不落盘）。
- 明文 `provider_key` 载荷为：`[{id, provider_id, key: "<明文>", label, hint_head, hint_tail}]`。其它字段仍在 `data.provider_keys` 里（不含 key）。
- AAD = `{format, format_version, app, options, secrets.kdf, secrets.cipher}` 的规范化 JSON（键排序、无空白、UTF-8）。
- 导入时先校验结构，再要求口令解密；口令错误只报一次通用错误，不重试爆破。

## 6. 待决问题（M3 前必须定）

1. 导出文件是单个 JSON（内嵌 base64）还是 zip 包（JSON + 附件目录）？当前 schema 按单 JSON 设计。2GB 单 JSON 在低端 Android 上解析需要流式 parser（不能 `json.load` 全量），这会显著增加工作量。**建议：默认单 JSON（≤200MB），超过 200MB 时自动切 zip。**
2. `settings` 是否导出设备相关项（如窗口位置、代理配置）？当前导出全部，导入时按白名单过滤设备相关键。需人类确认。
3. 是否需要"仅导出文本"的轻量模式（Markdown），与无损格式并存。M3 定。
