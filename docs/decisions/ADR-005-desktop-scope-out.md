# ADR-005 — 桌面端退出范围（2026-11-23）

- 状态：**Accepted**（人类指令）
- 影响范围：仓库范围、G1 验收判据、契约的适用面

## 1. 背景

2026-11-23 人类指令原文：「你以后不要负责本项目的电脑端了，直接删掉与电脑端相关的全部代码」。

此前 Windows 端已交付：协议层（73 测试）、Compose 对等的 WinUI 界面、Markdown 渲染（94 测试）、SQLite 持久化（108 测试）、暗色与消息操作（122 测试）、演示录制。这些全部作废，不是"暂停"。

## 2. 决策

1. **桌面端（Windows / C# / WinUI 3 / .NET）退出本项目范围。** Tech Lead 不再负责，也不再接受相关任务单。
2. 相关代码从工作树与索引删除：`windowsApp/**`（114 个跟踪文件）、`NeoChat.sln`、`Directory.Build.props`、`.github/workflows/windows.yml`、`tools/demo/capture_demo.ps1`、`tools/demo/build_demo_gif.py`。
3. **保留可恢复快照**：删除前的完整状态由 git tag `archive/windows-side-2026-11-23` 与同名分支指向，随时可 `git checkout archive/windows-side-2026-11-23 -- windowsApp` 取回。
4. 保留不删除：`spec/**`（平台中立契约）、`docs/**`（含历史证据与评审记录）、`androidApp/**`、`tools/spec-lint`、`tools/conformance`、`tools/schema-diff`、`tools/demo/mock_openai_sse.py`（安卓经 `adb reverse` 也能用）。

## 3. 后果

**正面**：范围收敛到一端，交付与验证链变短；不再为 WinUI 的打包工具链、Appx MSB4062 这类环境问题付出时间。

**负面（必须记账）**：

1. **G1 的核心判据失效。** 原判据是"同一批畸形 SSE 样本，双端解析结果逐字节一致"。只剩一端后，这条不成立。替代判据：与 `spec/conformance` 的**金样本 `expected.jsonl` 逐字节一致**，且 `tools/conformance` 的桩实现作为第三方参照。这条替换必须写进 G1，否则验收会退化成"自己跟自己比"。
2. 契约里所有"双端"表述（`stream-events.md` 的 I 系列、`providers.md` §8 一致性判据、`render-model.md` 的 L1~L4 等价）在实现层只剩一个消费者。**这些条款不删**——它们仍是未来任何第二实现（包括 dsh-Go 侧）的验收标准——但必须注明"当前仅安卓实现，另一端空缺"。
3. 桌面端的 8 条已知差距（公式降级、MSB4062、悬停未验等）随代码一起作废，不再进技术债表。

## 4. 复审触发条件

- 人类重新纳入桌面端（届时从 `archive/windows-side-2026-11-23` 恢复，并按当时的契约版本适配）
- 出现第二个实现（Dart / Web / 其他），G1 的判据需重新定义
