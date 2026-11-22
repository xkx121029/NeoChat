# tools/conformance — 样本生成器 · runner · 比对器

契约见 `spec/conformance/CASES.md`（§0 runner 契约）与 `spec/stream-events.md`。本目录只做三件事：
按契约生成 78 个样本、把样本字节喂给"被测实现"、比对事件序列。

## 跑

```bash
python tools/conformance/gen_samples.py            # 生成 samples/ + redaction-vectors.jsonl（确定性）
python tools/conformance/gen_samples.py --hash     # 只打印目录树 sha256
python tools/conformance/gen_samples.py --manifest # 打印 78 行清单（ID + 文件数）

python tools/conformance/runner.py --impl stub     # 桩实现跑全 78 条
python tools/conformance/runner.py --impl stub --cases FRAME-001,OAI-003
python tools/conformance/runner.py --impl tools/conformance/fixtures/stub_duplicate_done.py --cases FRAME-006
python tools/conformance/runner.py --verify-redaction spec/conformance/redaction-vectors.jsonl

python tools/conformance/compare.py --runs build/conformance/stub
python tools/conformance/compare.py --runs build/conformance/stub build/conformance/android
python tools/conformance/compare.py --runs A B --cases FRAME-001 --json build/cmp.json

python -m unittest discover -s tools/conformance/tests
```

退出码：全 pass 为 0，任何 fail / differ 为 1；`skipped` 既不算 pass 也不算 fail。

## 产物

| 路径 | 内容 |
| --- | --- |
| `spec/conformance/samples/<CASE>/stream.sse` | 原始 HTTP body 字节（未解压；FRAME-013 是 gzip 字节） |
| `spec/conformance/samples/<CASE>/meta.json` | `case/layer/protocol/http_status/http_headers/provider/chunk_pattern/expect_error/asserts/...` |
| `spec/conformance/samples/<CASE>/expected.jsonl` | 完整事件序列（`expect_error` 用例也含末尾 `failed` 帧） |
| `spec/conformance/samples/<CASE>/payload*.json` | EXPORT 组：导入文件（`layer=import`，无 SSE 流） |
| `build/conformance/<impl>/<CASE>/events.jsonl` | 键排序、无空白、UTF-8 无 BOM、每行一个事件、行尾 `\n` |
| `build/conformance/<impl>/<CASE>/exit.json` | `{ok, case, status, error, asserts[], events, socket_closed, eof_reached}` |
| `build/conformance/<impl>/summary.json` | 逐用例状态、事件数、events.jsonl 的 sha256、跳过原因归类 |

## 判据（compare.py）

1. 各端 `events.jsonl` 的 SHA-256 必须相同；2. 与 `expected.jsonl` 逐行相同（比对前剔除
`failed.error.trace_id` / `request_id`）；3. `expect_error` 命中且 `failed` 之后无事件（I3）；
4. `meta.asserts` 每条都要在 `exit.json.asserts` 里有 `{id, ok, actual}`，缺条目判失败。

`db` / `socket` / `perf` 三类断言当前一律记 `skipped` 并写明原因（需要落库、`activeStreamCount()` 钩子、
性能测量），**不记 pass**。`layer=render|import` 的用例同样记 skipped。

## meta.json 的扩展键（本 runner 用，契约之外）

`abort_at_byte`（TCP RST 位置）、`simulate_idle_ms` + `test_config.read_idle_ms`（加速 idle）、
`cancel_when.after_events`（取消时机）、`cumulative_stream`（前缀累积端点）、`duplicate`（重复 chunk 去重）、
`content_encoding`（gzip）、`expect.socket_closed`、`layer/layer_note/render_asserts/import_files`。

## 已知边界（未验证，卡在哪）

- 桩实现只覆盖 L1/L2（字节 → StreamEvent），RENDER（RenderModel）与 EXPORT（导入器）两组共 19 条
  断言无法由它判定，故记 skipped；`CHUNK` 契约只提供 `whole/bytes:n/fixed:n/random:seed`，
  "逐 10 行喂入"这类按行喂入的形态用 `bytes:n` 近似，`feed_hint` 只作记录。
- `errors.*` 的 `title_key`/`causes`/`actions` 由 `tools/conformance/errors_model.py` 统一产出，双端须复用同一张表。
- perf 不测量（M0-QE-002 明确不做），归 M0-QE-003 基准框架。
