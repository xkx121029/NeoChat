# ADR-004 — 全文搜索：SQLite FTS5 的可得性与分词

- 状态：**Accepted**（2026-11-24，实测驱动）
- 影响范围：存储层、搜索、APK 体积、`spec/schema/v1.sql` 的 FTS 注释

## 1. 背景：一条被实测否证的前提

`spec/schema/v1.sql` 原先隐含"Android 上 FTS5 一定可用（API 24 起）"。实测否定：

```
adb shell sqlite3 --version            -> 3.32.2
adb shell "CREATE VIRTUAL TABLE t USING fts5(a);" -> Error: no such module: fts5
```

AOSP API 33 模拟器的系统 SQLite **没有编 FTS5**。这不是模拟器特例：Android 的 SQLite 由 ROM 编译，是否带 FTS5 取决于厂商与版本，官方从未承诺。换言之，**依赖系统 SQLite 的 FTS5 就是把"搜索能不能用"交给 ROM**。

## 2. 决策

1. **自带 SQLite**：Android 侧改用随 APK 分发的 SQLite（`androidx.sqlite:sqlite-bundled` 或等价物），确保 FTS5 与 `unicode61` 分词器稳定可用。代价：APK +2~4 MB，**接受**——搜索是 MUST 能力，不该由 ROM 决定。
2. **DDL 不改**：`message_fts` 的建表语句、`tokenize='unicode61 remove_diacritics 2'` 保持原样；变的是"用谁的 SQLite 执行它"。
3. **CJK 分词保持二元组（bigram）临时口径**：写入与查询用同一 `SearchText` 函数。已知局限：查 `文章要点` 不会命中 `这篇文章的要点`（跨字间歇）。这是**明确记录的功能边界**，不是缺陷：换成真正的中文分词器需要词典与体积（数百 KB~数 MB），放到 M3 之后再评估。
4. 设备侧验收：androidTest 的 `MessageSearchTest` 保留；模拟器无 FTS5 时它必须**跳过并明确标记 skipped+原因**，不得静默通过。宿主机 FTS5 3.45.1 上的同链路验证（写入/2 字查询/4 字查询/删除后不再命中）作为补充证据。

## 3. 后果

**正面**：搜索结果不依赖设备；`v1.sql` 的契约不变。

**负面**：
- APK 体积增加（当前 14.85 MB，预计到 17~19 MB），仍在 ≤25 MB 的卓越线内。
- 自带 SQLite 与系统 SQLite 的版本差异会让极少数 SQL 行为不同（如 `PRAGMA` 默认值），需在启动时固定那五个 PRAGMA（已在 DDL 头部声明）。
- bigram 的跨字间歇问题是用户可感知的检索盲区，产品文案不得宣称"全库可搜"。

## 4. 复审触发条件

- M3 引入真正的中文分词（届时评估词典体积与许可）
- 出现需要 FTS5 之外能力（如 `trigram`、自定义 tokenizer）的需求
- APK 体积逼近 25 MB
