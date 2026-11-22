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
- 默认 `inline`：字节以 base64 内嵌。单文件上限 `size_bytes ≤ 20MB`；超限的附件只带元数据并标 `content_omitted=true`（该字段已进 schema，仅在 inline 模式出现；`content_omitted=true` 时 `content_b64` 必须缺失）。
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
| 错误路径 | MUST 用 JSON Pointer（如 `/data/attachments/3/local_path`），双端同格式 | 报告逐条列出 |

## 5. 密钥导出（与 ADR-002 对应）

- `include_secrets=true` 时必须有 `secrets` 块，且用户提供口令（不落盘）。
- 明文 `provider_key` 载荷为：`[{id, provider_id, key: "<明文>", label, hint_head, hint_tail}]`。其它字段仍在 `data.provider_keys` 里（不含 key）。
- **AAD 的唯一定义就是本节这一句**：`{format, format_version, app, options, secrets.kdf, secrets.cipher}` 的规范化 JSON（键排序、无空白、UTF-8）。`export-v1.json` 的 `ciphertext_b64.description` 与 ADR-002 都以本节为准；任何一方增删一项都会让 GCM 校验跨端失败。
- 导入时先校验结构，再要求口令解密；口令错误只报一次通用错误，不重试爆破。

## 6. v1 的格式决定（不再留待决）

1. **容器：只有单个 JSON 文件。** 不实现 zip，不做「超过某大小自动切 zip」——那会让导入端必须先探测容器类型，而 schema 里没有容器字段，两端必然对同一文件得出不同结论。
2. **大小上限：200 MB**，超过则拒绝导出并提示按会话范围分批导出。这同时是安全边界与内存边界：`export` 与 `import` 都 MUST 用流式 parser，禁止把整个文件读成一个字符串再解析。
3. **`settings` 导出全部键**，导入时按白名单过滤设备相关键（窗口位置、显示器、代理凭据句柄、安装路径）。白名单常量两端各一份，并配共享测试向量。
4. **不提供「仅导出文本」模式**：Markdown 导出属会话级导出功能（M3），不属本格式。

## 7. v1 明确不做

| 项 | 原因 |
| --- | --- |
| zip 容器 | 导入端需探测容器类型，schema 无字段，双端会分歧 |
| 增量/差分导出 | 需要基线版本管理，与「无损往返」判据冲突 |
| 附件按 path 外部引用 | 跨设备路径无意义，且是路径穿越攻击面 |

## 8. 导入时的密钥重建

`provider_key.key_ref` 是**本地平台密钥库的句柄**，导出文件里不包含（`required` 里也没有）。导入时 MUST 逐行为每个 key 重新创建密钥库条目并写入新的 `key_ref`；`UNIQUE(provider_id, key_ref)` 因此不会冲突。若文件不含 `secrets` 块，导入后的 key 只有 `hint_head`/`hint_tail` 与元数据，状态标为「需要重新录入密钥」，UI 明确提示，不静默标为可用。

## 9. 空集合与 scope

- `options.scope='settings'` 时 `data` 仍必须存在五个必填集合，内容为空数组（`required` 不因 scope 放宽）。
- 导入端 MUST NOT 把「集合缺失」与「集合为空」视为等价：缺失即校验失败。
