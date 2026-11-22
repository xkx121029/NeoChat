# 许可政策（2026-11-22 起）

## 本项目

NeoChat 以 **GNU AGPL-3.0-or-later** 开源（见根目录 `LICENSE`）。所有提交默认按该许可授权。

## 依赖白名单（AGPL-3 项目的兼容性判定）

| 宽松许可（可直接用） | 弱/强 copyleft（可用，需保留声明） | 禁止 |
| --- | --- | --- |
| MIT · Apache-2.0 · BSD-2/3-Clause · ISC · Zlib · Unlicense · MPL-2.0 | LGPL-3.0 · GPL-3.0 · GPL-2.0-or-later · AGPL-3.0 | **GPL-2.0-only**（与 AGPL-3 不兼容）· SSPL · BUSL · CC-BY-NC 类 · 无许可声明 |

说明：
- AGPL-3 与 GPL-3 兼容（AGPL-3 第 13 条），与 GPL-2.0-only **不兼容**，这是唯一需要机器拦截的常见许可。
- Apache-2.0 兼容；其专利条款不影响 AGPL 分发。
- 无法自动识别许可证的依赖进"待评估"清单，人工判定后才可合入。

## 参照外部代码的引入规则（含 ccch1mneyyy/dsh-Go）

1. 只允许参照**设计、结构与算法思路**。
2. 直接复制代码必须：原仓库许可兼容（同为 AGPL-3 即可）、文件头标注来源 URL + commit sha、`THIRD-PARTY.md` 记一条。
3. 不标注的复制视为许可污染，按缺陷处理。
4. 参照对象类型系统不同（dsh-Go 是 Dart）时，只搬概念，不搬实现细节。

## 检查方式

`ci/license` 检查依赖清单与白名单并产出 SBOM；CI 不放行"待评估"项。
