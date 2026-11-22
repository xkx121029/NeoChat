# ADR-002 — 密钥与数据安全

- 状态：**Accepted**（2026-11-22 人类裁决，见 [RULINGS-2026-11-22.md](RULINGS-2026-11-22.md)）
- 日期：2026-11-22
- 影响范围：Android 平台层、Windows 平台层、存储层、导出、日志、崩溃、诊断

## 1. 背景

三条硬要求：(a) 密钥绝不明文落盘；(b) 密钥可跨端导出导入（M12 二维码/文本串，双端互通）；(c) 数据库可选整体加密，开启后启动需生物识别/系统认证。

(a) 与 (b) 存在真实冲突：Keystore/DPAPI 封装的密文**不能跨设备解密**，因此"密钥导出"必须走另一条密钥派生路径，否则双端互通不可能。

## 2. 候选方案

### 2.1 密钥静态存储

| 方案 | Android | Windows | 结论 |
| --- | --- | --- | --- |
| 明文文件 | — | — | 否决（违反硬要求） |
| 自研 AES 密钥管理 | — | — | 否决（禁自研加密） |
| 平台密钥库封装 | Keystore AES-GCM（StrongBox 优先，回退 TEE/软件实现） | DPAPI 当前用户范围（`CryptProtectData`，无 `CRYPTPROTECT_LOCAL_MACHINE`） | **采用** |
| 用户口令派生 | Argon2id（或 PBKDF2-HMAC-SHA256，见下） | 同 | **采用，仅用于导出** |

采用组合：**静态 = 平台密钥库封装；导出 = 用户口令派生。**

### 2.2 导出密钥的加密（解开 (a)(b) 冲突）

- 导出时必须让用户设置一个**导出口令**（≥10 字符，强度提示，不落盘、不入日志）。
- 派生：`Argon2id`（m=64MiB, t=3, p=1），若平台库不可用则回退 `PBKDF2-HMAC-SHA256`（iterations ≥ 600000），并在导出文件头明示算法与参数，跨端按头部参数解密。
- 加密：AES-256-GCM，12 字节随机 nonce，认证标签 16 字节。**AAD 的唯一定义在 `spec/schema/export-v1.notes.md` §5**：`{format, format_version, app, options, secrets.kdf, secrets.cipher}` 的规范化 JSON。本 ADR 不复述该定义——三处各写一份正是跨端解密失败的成因（评审 MF-X2）。
- 失败语义：口令错误 → 统一报"口令不正确或文件已损坏"，不区分（防 oracle）。
- 二维码方案限制：单个二维码容量不足时按分片二维码序列导出，每片含序号 + 总片数 + 完整密文的 SHA-256 校验。
- **人类裁决（2026-11-22）：只走口令导出，明确接受“每次导出多输一次口令”这一代价。** 设备内 Keystore/DPAPI 密文不可跨机复制（跨设备解不开，这是平台设计而非实现缺陷），因此跨设备唯一的密钥迁移路径就是口令派生。
- 该代价必须写进产品文案，不允许藏在文档里：导出页明示“需要设置导出口令，导入时需再次输入”；导入失败不区分“口令错”与“文件损坏”。

### 2.3 数据库整体加密（可选开关）

- SQLite 用 SQLCipher（Android `net.zetetic:sqlcipher-android`，Windows `SQLitePCLRaw.bundle_e_sqlcipher`），密钥由平台密钥库封装；开启时额外要求生物识别/系统认证解锁。
- 默认**关闭**（性能与迁移成本）；开启后升级路径必须实测（SQLCipher 与 FTS5 兼容、迁移四件套）。
- 备选：若 SQLCipher 在 Windows 侧包体/许可证不可接受（SQLCipher 社区版 BSD 但需自编译），退化为"仅密钥与记忆加密 + 数据库文件 ACL"，并登记为技术债。**M3 前必须定案。**

## 3. 密钥生命周期

| 阶段 | 规则 |
| --- | --- |
| 录入 | 输入框 `FLAG_SECURE` / `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`；粘贴后 30s 自动清空剪贴板并提示 |
| 静态 | 平台密钥库封装后存入 `provider_key.key_ref` 指向的项；DB 内只存 `hint_head4 + hint_tail4` |
| 使用 | 仅在传输层构造请求头时解密到内存；字符串用可清空容器（Android `CharArray`/`ByteArray` 手动清零，Windows `SecureString` 或 `CryptographicOperations.ZeroMemory`） |
| 日志 | 永不出现在日志/异常/崩溃/诊断包；脱敏只允许首尾各 4 位 |
| 轮换 | 多 Key 轮换、健康评分、失败冷却、单 Key 限流隔离（M2） |
| 销毁 | 用户删除 Key → 立即删除密钥库条目 + DB 记录；"清空数据"→ 密钥库全清 + DB 重建 + 导出提醒 |

## 4. 暴露面清单（每次 Gate 逐个验证）

1. 磁盘文件（DB、设置、导出、临时目录、缓存、日志文件）
2. 内存转储（崩溃 dump、hprof、WER minidump）
3. 日志与遥测（默认零遥测）
4. 崩溃上报（默认关闭；开启时先展示字段清单）
5. 导出文件（配置导出、会话导出、诊断包）
6. 剪贴板
7. 截屏与录屏（密钥页禁截屏；任务切换器缩略图遮罩）
8. 网络（只发往用户配置的 provider 端点；不内置任何中转）
9. 备份（Android `allowBackup=false`；Windows 不写 Roaming 明文）
10. 第三方 SDK（禁用任何会采集内容的 SDK；依赖白名单制）

## 5. 扫描方案

`tools/secret-scan/`（M0-QE-004）：
- 熵扫描 + 已知前缀（`sk-`, `sk-ant-`, `AIza`, `gsk_`, `xai-`）+ 本地密钥库已知值的精确匹配（最重要：用测试密钥的真实值去 grep 全仓与全部产物）。
- 覆盖范围：源码、构建产物、日志目录、DB 文件、诊断包产物、UI 测试录屏字幕。
- CI 每次 PR 跑；Gate 前额外跑一次全量并归档报告到 `docs/security/`。
- 判据：命中数 = 0。任何命中即阻塞，无"已知误报"豁免（误报须改扫描规则并留记录）。

## 6. 后果

**正面**：跨端导出可行且不依赖任何自定义密码学；静态存储用平台原语。

**负面**：
- 导出强制口令，用户多一步操作，产品文案必须解释清楚。
- Argon2id 在低端 Android 上需 200~400ms，导出流程要显示进度。
- SQLCipher 引入额外包体（Android +1.5~2.5MB；Windows +2~3MB）与构建复杂度。

## 7. 复审触发条件

- 出现任何一次密钥明文命中（触发 P0 复盘）
- StrongBox 在目标机型覆盖率 <50% 且 TEE 回退有兼容问题
- 导出格式因新 provider 需要携带非字符串凭证（如 JSON 私钥）
