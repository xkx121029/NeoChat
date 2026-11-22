"""conformance runner：按 meta.json 的 chunk_pattern 把样本字节切片喂给被测实现。

用法：
    python runner.py --samples spec/conformance/samples --impl stub --out build/conformance
    python runner.py --impl tools/conformance/fixtures/stub_emits_empty_delta.py --cases FRAME-011
    python runner.py --verify-redaction spec/conformance/redaction-vectors.jsonl

产物（每个用例）：<out>/<impl>/<CASE>/events.jsonl（键排序、无空白、UTF-8 无 BOM）
与 exit.json（{ok, case, error?, status, asserts[], ...}），以及 <out>/<impl>/summary.json。
退出码：全 pass 为 0，有 fail 为 1（skipped 不算 pass，也不算 fail）。
"""
import argparse
import hashlib
import importlib
import importlib.util
import io
import json
import os
import random
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
try:  # Windows 控制台默认 cp936，会把中文输出打成乱码
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass
sys.path.insert(0, HERE)

import errors_model  # noqa: E402
import redactor  # noqa: E402

TIMEOUT = "timeout"
RESET = "reset"


def canonical(obj, top=True):
    """规范化：剔除非确定性字段、键排序、无空白。"""
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            if top and k == "error" and isinstance(v, dict):
                v = {kk: vv for kk, vv in v.items()
                     if kk not in ("trace_id", "request_id")}
            out[k] = canonical(v, top=False)
        return out
    if isinstance(obj, list):
        return [canonical(x, top=False) for x in obj]
    return obj


def dumps(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


# ---------------------------------------------------------------- 传输层
class SampleReader(object):
    """把样本字节按 chunk_pattern 切片；abort_at_byte 处抛连接重置；末尾可模拟 idle。"""

    def __init__(self, data, meta):
        self.data = data
        self.closed = False
        self.eof_reached = False
        self.pos = 0
        pattern = (meta.get("chunk_pattern") or "whole").strip()
        self.chunks = list(self._chunks(data, pattern))
        self.abort_at = meta.get("abort_at_byte")
        self.stall_ms = meta.get("simulate_idle_ms")
        self._stalled = False

    @staticmethod
    def _chunks(data, pattern):
        if pattern == "whole":
            yield data
            return
        if pattern.startswith("bytes:") or pattern.startswith("fixed:"):
            n = int(pattern.split(":", 1)[1])
            for i in range(0, len(data), n):
                yield data[i:i + n]
            return
        if pattern.startswith("random:"):
            rng = random.Random(int(pattern.split(":", 1)[1]))
            i = 0
            while i < len(data):
                n = rng.randint(1, 64)
                yield data[i:i + n]
                i += n
            return
        raise ValueError("unknown chunk_pattern: %r" % pattern)

    def read(self, timeout_ms):
        if self.closed:
            return b""
        if self.abort_at is not None and self.pos >= self.abort_at:
            self.closed = True
            raise_reset()
        if self.chunks:
            chunk = self.chunks.pop(0)
            if self.abort_at is not None and self.pos + len(chunk) > self.abort_at:
                head = chunk[:self.abort_at - self.pos]
                self.pos = self.abort_at
                if head:
                    self.chunks.insert(0, chunk[len(head):] or b"")
                    if not self.chunks[-1]:
                        self.chunks.pop()
                    return head
                self.closed = True
                raise_reset()
            self.pos += len(chunk)
            return chunk
        self.eof_reached = True
        if self.stall_ms and not self._stalled:
            self._stalled = True
            time.sleep(min(self.stall_ms, 5000) / 1000.0)
            raise_timeout()
        return b""

    def close(self):
        self.closed = True


def raise_reset():
    raise RuntimeError(RESET)


def raise_timeout():
    raise RuntimeError(TIMEOUT)


# ---------------------------------------------------------------- 实现加载
def load_impl(spec):
    if spec == "stub":
        return importlib.import_module("stub_impl")
    if spec.endswith(".py"):
        if os.path.isabs(spec):
            path = spec
        elif os.path.exists(spec):
            path = os.path.abspath(spec)
        else:
            path = os.path.join(HERE, spec)
        name = "impl_" + os.path.basename(path).replace(".py", "")
        mod_spec = importlib.util.spec_from_file_location(name, path)
        mod = importlib.util.module_from_spec(mod_spec)
        mod_spec.loader.exec_module(mod)
        return mod
    return importlib.import_module(spec)


# ---------------------------------------------------------------- 驱动
def run_case(impl, case_dir):
    meta = json.load(io.open(os.path.join(case_dir, "meta.json"), encoding="utf-8"))
    stream = os.path.join(case_dir, meta.get("stream_file") or "stream.sse")
    data = b""
    if os.path.exists(stream):
        data = io.open(stream, "rb").read()
    reader = SampleReader(data, meta)
    adapter = impl.Adapter(meta, reader)

    # SampleReader 的超时/重置用 RuntimeError 标记，这里翻译成实现的异常类型
    orig_read = reader.read

    def read(timeout_ms):
        try:
            return orig_read(timeout_ms)
        except RuntimeError as exc:
            if str(exc) == TIMEOUT:
                raise impl.ReadTimeout()
            if str(exc) == RESET:
                raise impl.ConnectionReset()
            raise

    reader.read = read

    cancel_at = (meta.get("cancel_when") or {}).get("after_events")
    events = []
    try:
        if cancel_at == 0:
            adapter.cancel()
        for ev in adapter.events():
            events.append(canonical(ev))
            if cancel_at is not None and len(events) == cancel_at:
                adapter.cancel()
    except Exception as exc:  # 桩实现自身抛未捕获异常 = 该用例失败
        return meta, events, {"ok": False, "case": meta["case"],
                              "error": "%s: %s" % (type(exc).__name__, exc)}, reader, adapter
    return meta, events, None, reader, adapter


def eval_asserts(meta, reader, adapter, events):
    out = []
    for i, a in enumerate((meta.get("asserts") or {}).get("db") or []):
        out.append({"id": "db[%d].%s" % (i, a.get("check")), "ok": None, "actual": None,
                    "skipped_reason": "db hook not implemented by protocol stub (no persistence)"})
    for i, a in enumerate((meta.get("asserts") or {}).get("socket") or []):
        out.append({"id": "socket[%d].%s" % (i, a.get("expect")), "ok": None,
                    "actual": {"socket_closed": getattr(adapter, "socket_closed", False),
                               "eof_reached": reader.eof_reached},
                    "skipped_reason": "socket hook activeStreamCount() not available; "
                                      "stub reports socket_closed/eof_reached only"})
    for i, a in enumerate((meta.get("asserts") or {}).get("perf") or []):
        out.append({"id": "perf[%d].%s" % (i, a.get("metric")), "ok": None, "actual": None,
                    "skipped_reason": "perf not measured here (M0-QE-002 excludes perf; see M0-QE-003)"})
    expect = meta.get("expect") or {}
    if "socket_closed" in expect:
        actual = bool(getattr(adapter, "socket_closed", False))
        out.append({"id": "socket_closed", "ok": actual == bool(expect["socket_closed"]),
                    "actual": actual})
    return out


def compare_expected(meta, events, case_dir):
    """返回 (status, reason)。expected.jsonl 与 expect_error 二者必居其一。

    expected.jsonl 是完整事件序列（expect_error 用例也含 failed 帧）；
    expect_error 额外要求末帧为 failed(<code>) 且其后无事件（I3）。
    """
    exp_path = os.path.join(case_dir, "expected.jsonl")
    expected = []
    if os.path.exists(exp_path):
        with io.open(exp_path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line:
                    expected.append(canonical(json.loads(line)))
    if not os.path.exists(exp_path) and not meta.get("expect_error"):
        return "fail", "missing expected.jsonl and expect_error"
    if meta.get("expect_error"):
        if not events or events[-1].get("t") != "failed":
            return "fail", "expect_error=%s but last event is not failed" % meta["expect_error"]
        got = (events[-1].get("error") or {}).get("code")
        if got != meta["expect_error"]:
            return "fail", "expected failed(%s), got failed(%s)" % (meta["expect_error"], got)
    if expected == events:
        return None, None
    for i in range(max(len(expected), len(events))):
        e = expected[i] if i < len(expected) else None
        g = events[i] if i < len(events) else None
        if e != g:
            return "fail", "events.jsonl line %d: expected %s / got %s" % (
                i + 1, dumps(e) if e is not None else "<none>",
                dumps(g) if g is not None else "<none>")
    return "fail", "events.jsonl mismatch"


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--samples", default=os.path.join(HERE, "..", "..", "spec", "conformance", "samples"))
    ap.add_argument("--impl", default="stub")
    ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "build", "conformance"))
    ap.add_argument("--cases", default="", help="逗号分隔的用例 ID 过滤")
    ap.add_argument("--impl-name", default="", help="输出目录名（默认取 --impl 的文件名）")
    ap.add_argument("--verify-redaction", default="", help="只跑脱敏向量")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    if args.verify_redaction:
        return verify_redaction(args.verify_redaction)

    impl = load_impl(args.impl)
    name = args.impl_name or os.path.basename(args.impl).replace(".py", "")
    out_root = os.path.join(args.out, name)
    cases = sorted(d for d in os.listdir(args.samples)
                   if os.path.isdir(os.path.join(args.samples, d)))
    if args.cases:
        want = set(x.strip() for x in args.cases.split(",") if x.strip())
        cases = [c for c in cases if c in want]

    counts = {"pass": 0, "fail": 0, "skipped": 0}
    reasons = {}
    rows = []
    for case in cases:
        case_dir = os.path.join(args.samples, case)
        meta, events, fatal, reader, adapter = run_case(impl, case_dir)
        case_out = os.path.join(out_root, case)
        os.makedirs(case_out, exist_ok=True)
        body = "".join(dumps(e) + "\n" for e in events)
        with io.open(os.path.join(case_out, "events.jsonl"), "wb") as f:
            f.write(body.encode("utf-8"))
        asserts = [] if fatal else eval_asserts(meta, reader, adapter, events)
        status = "fail"
        reason = None
        if fatal:
            reason = fatal["error"]
        elif meta.get("layer") == "import":
            status = "skipped"
            reason = ("import layer: needs both importers/exporters; protocol stub emits "
                      "no StreamEvent")
        else:
            status, reason = compare_expected(meta, events, case_dir)
            if status is None:
                bad = [a for a in asserts if a["ok"] is False]
                skipped = [a for a in asserts if a["ok"] is None]
                if bad:
                    status = "fail"
                    reason = "assert %s failed: actual=%s" % (bad[0]["id"], dumps(bad[0]["actual"]))
                elif skipped:
                    status = "skipped"
                    reason = skipped[0]["skipped_reason"]
                elif meta.get("layer") == "render":
                    status = "skipped"
                    reason = ("render layer: needs both Markdown parsers producing "
                              "RenderModel; stub emits deltas only")
                else:
                    status = "pass"
        counts[status] += 1
        if status != "pass":
            reasons.setdefault(reason or "unknown", []).append(case)
        exit_obj = {
            "ok": status == "pass",
            "case": case,
            "status": status,
            "error": fatal["error"] if fatal else (None if status == "pass" else reason),
            "impl": name,
            "asserts": asserts,
            "events": len(events),
            "invalid_utf8_bytes": getattr(adapter, "invalid_utf8_bytes", None),
            "socket_closed": bool(getattr(adapter, "socket_closed", False)),
            "eof_reached": bool(reader.eof_reached),
        }
        with io.open(os.path.join(case_out, "exit.json"), "w", encoding="utf-8",
                     newline="\n") as f:
            f.write(dumps(exit_obj) + "\n")
        rows.append({"case": case, "status": status, "events": len(events),
                     "sha256": hashlib.sha256(body.encode("utf-8")).hexdigest(),
                     "reason": reason})

    summary = {"impl": name, "cases": len(cases), "counts": counts,
               "non_pass": reasons, "results": rows}
    with io.open(os.path.join(out_root, "summary.json"), "w", encoding="utf-8",
                 newline="\n") as f:
        f.write(json.dumps(summary, ensure_ascii=False, indent=2, sort_keys=True) + "\n")

    if not args.quiet:
        for r in rows:
            print("%-11s %-8s events=%-3d %s" % (r["case"], r["status"], r["events"],
                                                 "" if r["status"] == "pass" else r["reason"]))
        print("\nimpl=%s total=%d pass=%d fail=%d skipped=%d"
              % (name, len(rows), counts["pass"], counts["fail"], counts["skipped"]))
        for reason, ids in reasons.items():
            print("  %-60s %d  %s" % (reason[:60], len(ids), ",".join(ids[:6]) +
                                      ("..." if len(ids) > 6 else "")))
    return 0 if counts["fail"] == 0 else 1


def verify_redaction(path):
    ok = bad = 0
    with io.open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            v = json.loads(line)
            got = redactor.redact(v["input"])
            if got == v["expected"]:
                ok += 1
            else:
                bad += 1
                print("FAIL %s [%s]\n  input   =%s\n  expected=%s\n  actual  =%s"
                      % (v["id"], v["rule"], json.dumps(v["input"], ensure_ascii=False),
                         json.dumps(v["expected"], ensure_ascii=False),
                         json.dumps(got, ensure_ascii=False)))
    print("redaction vectors: %d ok, %d failed" % (ok, bad))
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
