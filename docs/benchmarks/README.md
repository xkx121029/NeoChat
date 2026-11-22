# docs/benchmarks/README.md — 基准测试方案

> 没有测量框架，每个 Gate 都缺证据来源，Gate 会退化为形式。因此本方案与契约同期建立（M0-QE-003）。

## 1. 原则

1. **可复现**：同 commit + 同设备 + 同脚本 → 结果落在同一数量级；每次运行记录 `build_id`、`commit`、`device`。
2. **原始数据入库**：JSONL 原始样本与汇总表一起归档，禁止只留汇总。
3. **网络可控**：默认用本地 mock SSE server 测量"排除网络"的指标；真实网络只用于端到端复测，单列记录。
4. **不预热混淆**：冷启动类指标单独报告首 3 次（真冷）与随后 17 次（系统已缓存），N=20。
5. **统计口径**：P50 / P95 / P99 按标注取值，配 min / max / 样本数。

## 2. 测量基准设备

| 端 | 规格 | 采集前状态 |
| --- | --- | --- |
| Android | 8GB RAM / 中端 SoC / 120Hz / Android 14+，具体机型见 `docs/benchmarks/devices.md` | 电量 60~80%、机身温度接近环境温度、飞行模式 + Wi-Fi、后台清空、关闭省电模式 |
| Windows | 4 核 / 8GB / 集显 / Windows 11 23H2+ | 接电源、高性能电源计划、关闭 Windows Update 活动、关闭浏览器 |
| 网络 | 100Mbps 家用宽带；另做一次 4G 复测 | 记录 RTT 与该次带宽实测值 |

## 3. 指标 → 测量方法

| 指标 | Android | Windows | 备注 |
| --- | --- | --- | --- |
| 首帧可见 / 首屏可交互 / 托盘唤起（三档） | Macrobenchmark `StartupTimingMetric` + `FrameTimingMetric` | ETW（WPR）：`AppLaunch` → 首帧呈现 → 列表可交互；托盘唤起单独打点 | 三档各自独立报告（ADR-001 §2.5）；冷启动前 3 次单列（真冷），随后 17 次另报 |
| 热启动恢复消息列表 | Macrobenchmark（进程存活，`HOME` 后返回） | 托盘唤起 → 列表首帧 | 与 ADR-001 §2.5 三档口径对应 |
| 首 token 上屏额外开销 | Perfetto 自定义 trace slice：`sse_first_byte` → `first_glyph_drawn` | ETW slice 同上 | 排除网络：用 mock server 固定 0 延迟 |
| 逐 token 帧间隔抖动 | `FrameTimingMetric` 的 frame duration 序列，算 P99 抖动（相邻差绝对值） | PresentMon 帧时长序列，同算法 | 抖动定义：|Δframe| 的 P99 |
| 长会话滚动掉帧 | Macrobenchmark `scrollVerticallyBy` + `FrameTimingMetric`，1000 条 | `ItemsRepeater` 程序化滚动 + PresentMon | 掉帧率 = 超过 2 帧预算的帧占比 |
| 渲染耗时（Mermaid/LaTeX/长消息） | 自定义 `Trace` 段（`render:*`），后台线程计时 | ETW 段 | 必须区分"后台解析/布局"与"UI 线程呈现" |
| 全文搜索 | 直接测 repository 层查询（10 万条数据集） | 同 | 冷/热各 10 次 |
| PDF 抽取 | 测 service 层，记录页数与耗时 | 同 | 附取消耗时 |
| 内存 | `dumpsys meminfo <pkg>` 的 TOTAL PSS；长时压测用 `am dumpheap` | `Process.WorkingSet64` + `dotnet-counters` 堆 | 100 会话常驻：加载后静置 60s 测 |
| 24h 压测 | 脚本每小时触发 50 轮会话切换 + 10 次流式，记录 PSS | 同 | 增长 = (末 - 首)/首 |
| CPU | `top -H -p <pid>` 采样 / Perfetto cpu 计数 | ETW `Process CPU` | 连续流式 60s 的均值 |
| 能耗 | `dumpsys batterystats` 前后差值（mAh）折算百分比 | — | 亮度固定、飞行模式 |
| 温升 | `dumpsys thermalservice` 的 `mStatus` 与电池温度 | — | 30 分钟流式，环境温度记录 |
| 空闲 CPU（托盘） | — | ETW / Perf counter，静置 10 分钟 | 只算本进程 |
| 安装包体积 | `bundletool get-size total` + 下载量估算 | MSIX 文件大小 + 单文件产物大小 | 不含附件与模型 |
| 崩溃自由会话率 | 内部统计（本地聚合，无外发）+ 测试脚本注入异常计数 | 同 | 分母为会话启动次数 |
| 一致性（conformance） | `events.jsonl` SHA-256 双端比对 | 同 | G1 核心判据 |

## 4. 原始日志格式（JSONL，一行一个样本）

```json
{"run_id":"2026-11-22T10:31:02Z-a7f3","case":"startup.cold.interactive","metric":"ms","value":1187,"t_start":1763807462000,"t_end":1763807463187,"build_id":"0.1.0-debug.42","commit":"a1b2c3d","device":"android:8GB:mid","os":"Android 14","notes":"cold#1","samples_index":0}
```

必填字段：`run_id, case, metric, value, unit, t_start, t_end, build_id, commit, device, os, notes`。
`unit` 闭集：`ms, bytes, percent, count, cpm`。

## 5. 汇总与归档

- `bench/summarize.py`：读 JSONL → 按 `case` 分组 → 输出 min/p50/p95/p99/max/n + 与地板/卓越线的差。
- 汇总表落 `docs/benchmarks/<date>-<milestone>.md`，原始 JSONL 落同名 `.jsonl.gz`。
- `docs/PROJECT_STATE.md` 的仪表盘只填汇总数字 + 指向这两份文件的路径。

## 6. 合成数据生成器规格

| 项 | 规格 |
| --- | --- |
| 规模档位 | small（100 会话 / 1 千消息）、large（1000 会话 / 10 万消息） |
| 消息长度分布 | 长尾：70% < 200 字符，20% 200~2000，8% 2000~20000，2% 20000~100000 |
| 内容构成 | 纯文本、含代码块（各语言占比固定）、含表格、含 LaTeX、含 Mermaid、中英混排（比例 6:3:1） |
| 分支结构 | 15% 的 assistant 消息有 2~4 个兄弟分支；树深 ≤ 30 |
| 附件引用 | 5% 的消息引用附件（图片/PDF/TXT），附件数 500，去重后按 sha256 |
| 用量记录 | 每条 assistant 消息一条，含估算标记 |
| 记忆与提示词 | 500 条记忆、200 条提示词 |
| 确定性 | 固定 seed，重复运行产出字节 SHA-256 相同 |
| 输出 | `bench/data/<seed>-<scale>/`（jsonl + attachments/） |

## 7. 与 Gate 的关系

| Gate | 基准要求 |
| --- | --- |
| G0 | 框架可跑 + mock server 可用 + 生成器可复现 |
| G1 | 首 token 开销、帧抖动、停止与重连（录屏） |
| G3 | 滚动、渲染、检索、导出往返 |
| G6 | 内存、CPU、能耗、温升 |
| G7 | 地板表逐项 + 24h 压测 + 包体 |
