"""比对两端（或多份）events.jsonl 的 SHA-256，并执行 meta.json 里的 asserts。

    python tools/conformance/compare.py --runs build/conformance/stub
    python tools/conformance/compare.py --runs build/conformance/stub build/conformance/android
    python tools/conformance/compare.py --runs A B --json build/conformance/compare.json

判定（CASES.md §0）：
  1. 各端 events.jsonl 的 SHA-256 必须相同；
  2. 与 samples/<CASE>/expected.jsonl 相同（若提供）；
  3. meta.json 的 expect_error 必须命中；
  4. meta.json 的每条 asserts 必须在 exit.json 里有 {id, ok, actual}；缺条目判失败；
     db / socket / perf 三类断言本 runner 无法执行时，该用例记 skipped-with-reason，
     **不记 pass**（不许静默通过）。
退出码：全部 pass 为 0，任何 fail / differ 为 1。
"""
import argparse
import hashlib
import io
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))


def dumps(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def canonical(obj, top=True):
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            if top and k == "error" and isinstance(v, dict):
                v = {kk: vv for kk, vv in v.items() if kk not in ("trace_id", "request_id")}
            out[k] = canonical(v, top=False)
        return out
    if isinstance(obj, list):
        return [canonical(x, top=False) for x in obj]
    return obj


def read_events(path):
    data = io.open(path, "rb").read()
    if data.startswith(b"\xef\xbb\xbf"):
        return data, None, "events.jsonl 带 BOM（必须 UTF-8 无 BOM）"
    text = data.decode("utf-8")
    events = []
    for i, line in enumerate(text.split("\n")):
        if not line:
            continue
        try:
            events.append(canonical(json.loads(line)))
        except ValueError as exc:
            return data, None, "events.jsonl 第 %d 行不是合法 JSON: %s" % (i + 1, exc)
    canon = "".join(dumps(e) + "\n" for e in events).encode("utf-8")
    if canon != data:
        return data, events, "events.jsonl 未规范化（需键排序、无空白、行尾 \\n）"
    return data, events, None


def assert_ids(meta):
    ids = []
    for group, items in sorted((meta.get("asserts") or {}).items()):
        for i, a in enumerate(items or []):
            name = a.get("check") or a.get("expect") or a.get("metric") or "?"
            ids.append("%s[%d].%s" % (group, i, name))
    return ids


def compare_one(case_dir, run_dirs, names):
    meta = json.load(io.open(os.path.join(case_dir, "meta.json"), encoding="utf-8"))
    case = meta["case"]
    row = {"case": case, "layer": meta.get("layer"), "status": "pass", "reason": None,
           "sha256": {}, "expected_match": None, "asserts": []}

    shas = {}
    for run, name in zip(run_dirs, names):
        path = os.path.join(run, case, "events.jsonl")
        if not os.path.exists(path):
            row["status"] = "fail"
            row["reason"] = "缺少 %s/%s/events.jsonl" % (name, case)
            return row
        data, events, err = read_events(path)
        shas[name] = hashlib.sha256(data).hexdigest()
        row["sha256"] = shas
        if err:
            row["status"] = "fail"
            row["reason"] = "%s: %s" % (name, err)
            return row

    if len(set(shas.values())) > 1:
        row["status"] = "differ"
        row["reason"] = "各端 SHA-256 不同: " + ", ".join(
            "%s=%s" % (n, shas[n][:16]) for n in names)
        return row

    got = []
    for line in io.open(os.path.join(run_dirs[0], case, "events.jsonl"), encoding="utf-8"):
        line = line.strip()
        if line:
            got.append(canonical(json.loads(line)))
    got = []
    for line in io.open(os.path.join(run_dirs[0], case, "events.jsonl"), encoding="utf-8"):
        line = line.strip()
        if line:
            got.append(canonical(json.loads(line)))
    if meta.get("layer") == "import":
        row["status"] = "skipped"
        row["reason"] = "import 层：需要双端导入/导出器，协议 runner 不产出 StreamEvent"
        return row

    got = []
    for line in io.open(os.path.join(run_dirs[0], case, "events.jsonl"), encoding="utf-8"):
        line = line.strip()
        if line:
            got.append(canonical(json.loads(line)))
    exp_path = os.path.join(case_dir, "expected.jsonl")
    expected = []
    if os.path.exists(exp_path):
        for line in io.open(exp_path, encoding="utf-8"):
            line = line.strip()
            if line:
                expected.append(canonical(json.loads(line)))

    row["expected_match"] = expected == got
    if meta.get("expect_error"):
        # expected.jsonl 是完整序列（含 failed 帧）；expect_error 额外要求末帧是 failed(code)
        if not got or got[-1].get("t") != "failed":
            row["status"] = "fail"
            row["reason"] = "expect_error=%s 但最后一帧不是 failed" % meta["expect_error"]
            return row
        code = (got[-1].get("error") or {}).get("code")
        if code != meta["expect_error"]:
            row["status"] = "fail"
            row["reason"] = "expect_error=%s，实际 failed(%s)" % (meta["expect_error"], code)
            return row
    if not row["expected_match"]:
        for i in range(max(len(expected), len(got))):
            a = expected[i] if i < len(expected) else None
            b = got[i] if i < len(got) else None
            if a != b:
                row["status"] = "fail"
                row["reason"] = "events.jsonl 第 %d 行: expected %s / got %s" % (
                    i + 1, dumps(a) if a is not None else "<none>",
                    dumps(b) if b is not None else "<none>")
                break
        return row

    skipped_reasons = []
    for run, name in zip(run_dirs, names):
        exit_path = os.path.join(run, case, "exit.json")
        if not os.path.exists(exit_path):
            row["status"] = "fail"
            row["reason"] = "缺少 %s/%s/exit.json" % (name, case)
            return row
        exit_obj = json.load(io.open(exit_path, encoding="utf-8"))
        got = {a["id"]: a for a in exit_obj.get("asserts") or []}
        for aid in assert_ids(meta):
            a = got.get(aid)
            row["asserts"].append({"id": aid, "host": name,
                                   "ok": None if a is None else a.get("ok")})
            if a is None:
                row["status"] = "fail"
                row["reason"] = "%s: exit.json 缺断言条目 %s" % (name, aid)
                return row
            if a.get("ok") is False:
                row["status"] = "fail"
                row["reason"] = "%s: 断言 %s 失败，actual=%s" % (
                    name, aid, dumps(a.get("actual")))
                return row
            if a.get("ok") is None:
                skipped_reasons.append("%s: %s（%s）" % (name, aid, a.get("skipped_reason")))

    if meta.get("layer") == "import" and not (meta.get("asserts") or {}).get("db"):
        skipped_reasons.append("import 层断言需双端导入器，协议 runner 不覆盖")
    if meta.get("layer") == "render":
        skipped_reasons.append("render 层断言需双端 Markdown 解析器（RenderModel）")
    if skipped_reasons:
        row["status"] = "skipped"
        row["reason"] = skipped_reasons[0] + ("（另有 %d 条）" % (len(skipped_reasons) - 1)
                                              if len(skipped_reasons) > 1 else "")
    return row


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--samples", default=os.path.join(ROOT, "spec", "conformance", "samples"))
    ap.add_argument("--runs", nargs="+", default=[os.path.join(ROOT, "build", "conformance",
                                                              "stub")])
    ap.add_argument("--json", default="")
    ap.add_argument("--cases", default="", help="逗号分隔的用例 ID 过滤")
    args = ap.parse_args(argv)

    names = [os.path.basename(os.path.normpath(r)) for r in args.runs]
    cases = sorted(d for d in os.listdir(args.samples)
                   if os.path.isdir(os.path.join(args.samples, d)))
    if args.cases:
        want = set(x.strip() for x in args.cases.split(",") if x.strip())
        cases = [c for c in cases if c in want]
    rows = []
    counts = {"pass": 0, "fail": 0, "differ": 0, "skipped": 0}
    for case in cases:
        row = compare_one(os.path.join(args.samples, case), args.runs, names)
        rows.append(row)
        counts[row["status"]] += 1

    width = max(len(n) for n in names)
    print("%-11s %-8s %s" % ("CASE", "STATUS", "  ".join(n.ljust(12) for n in names)))
    for r in rows:
        shas = "  ".join((r["sha256"].get(n, "-")[:12]).ljust(12) for n in names)
        print("%-11s %-8s %s%s" % (r["case"], r["status"], shas,
                                   "" if r["status"] == "pass" else "  " + (r["reason"] or "")))
    print("\nruns=%s cases=%d pass=%d fail=%d differ=%d skipped=%d"
          % (",".join(names), len(rows), counts["pass"], counts["fail"],
             counts["differ"], counts["skipped"]))
    if args.json:
        with io.open(args.json, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps({"runs": names, "counts": counts, "rows": rows},
                               ensure_ascii=False, indent=2, sort_keys=True) + "\n")
        print("json=%s" % args.json)
    return 0 if (counts["fail"] == 0 and counts["differ"] == 0) else 1


if __name__ == "__main__":
    sys.exit(main())
