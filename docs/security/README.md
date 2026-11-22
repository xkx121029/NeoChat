# docs/security/ — 安全工件与结论

## 必须归档的内容

| 工件 | 频率 | 判据 |
| --- | --- | --- |
| 密钥明文扫描报告 | 每个 PR（CI）+ 每个 Gate（全量） | 命中数 = 0 |
| SBOM | 每次构建 | 存在且覆盖传递依赖 |
| 许可证清单与白名单校验 | 每个 PR | 零黑名单命中 |
| 依赖漏洞扫描 | 每阶段 | 无 Critical/High 未处置 |
| 可复现构建对照 | 每次发布 | 哈希相同或原因清单完整 |
| 暴露面清单核查记录 | 每个 Gate | ADR-002 §4 的 10 项逐项有结论 |

## 当前状态

M0：扫描器（M0-QE-004）与 SBOM（M0-QE-005）尚未实现。**在此之前不得宣称任何"密钥安全"结论。**

## 许可证白名单（2026-11-22 起，AGPL-3 项目）

见 `docs/security/LICENSE-POLICY.md` 与 `docs/decisions/ADR-004-license-agpl.md`。

- 可直接用：MIT · Apache-2.0 · BSD-2/3-Clause · ISC · Zlib · Unlicense · MPL-2.0
- 可用但保留声明：LGPL-3.0 · GPL-3.0 · GPL-2.0-or-later · AGPL-3.0
- 禁止：**GPL-2.0-only**（与 AGPL-3 不兼容）· SSPL · BUSL · CC-BY-NC 类 · 无许可声明
