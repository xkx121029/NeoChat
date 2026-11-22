# NeoChat

双原生 BYOK（自带密钥）大模型对话客户端。

- Android：Kotlin 2.x + Jetpack Compose + Material 3，minSdk 26
- Windows：C# 13 / .NET 9 + WinUI 3（Windows App SDK）
- 不使用 Electron；不以 WebView 作为主渲染路径（含 Mermaid 与 LaTeX）

## 当前状态

**阶段 M0：契约冻结 + 质量工程搭建。** 契约草案已落盘（DRAFT，待交叉评审），尚无业务代码，按 M0 规则不放出任何 UI 代码。

唯一状态事实来源：[docs/PROJECT_STATE.md](docs/PROJECT_STATE.md)。质量三角待人类拍板：[docs/TRIANGLE.md](docs/TRIANGLE.md)。

## 文档地图

| 位置 | 内容 | 权威性 |
| --- | --- | --- |
| `spec/` | 协议、错误模型、DDL、导出 Schema、RenderModel、provider 契约 | **唯一事实来源**，仅 Tech Lead 可改 |
| `spec/conformance/CASES.md` | 畸形流与一致性用例清单 | 唯一事实来源 |
| `docs/PROJECT_STATE.md` | 阶段、Gate、仪表盘、任务、阻塞、债务 | 状态唯一事实来源 |
| `docs/decisions/ADR-###` | 不可逆决策与其取舍（含负面后果） | 只增不删，推翻则新开 |
| `docs/benchmarks/` | 测量方案与原始基准日志 | 数字唯一来源 |
| `docs/reviews/` | 验收记录、返工指令、对抗评审、录屏 | 验收唯一来源 |
| `docs/security/` | 密钥明文扫描、SBOM、许可证、漏洞报告 | 安全唯一来源 |
| `docs/M0-TASKS.md` | M0 任务单（八段模板） | 任务边界唯一来源 |
| `docs/RISKS.md` | 风险登记册（含预决定应对） | 风险唯一来源 |
| `docs/DECISIONS_NEEDED.md` | 需人类拍板事项，分三档 | 决策待办唯一来源 |

同一事实只允许存在于上表指定的一个位置。发现重复描述即删除冗余副本并指向权威位置（准则 6）。

## 阅读顺序（新人或上下文重置后）

1. `docs/PROJECT_STATE.md`
2. `docs/TRIANGLE.md`
3. 最近 3 份 ADR
4. 当前任务相关的 `spec/` 章节（核对版本号）
5. `docs/M0-TASKS.md` 中与己相关的任务单

## 铁律（完整版见 [CONTRIBUTING.md](CONTRIBUTING.md)）

1. 改 `spec/` 之前不许写实现；spec 变更 = 升版本 + 广播 + 双端适配 + 全量 conformance。
2. 交付必须附命令原文输出，只给结论的交付直接打回。
3. 不使用形容词验收。禁语清单见 CONTRIBUTING。
4. 领域层出现 `if (protocol == "anthropic")` 即返工。
5. 密钥明文出现次数必须为 0，一票否决。
