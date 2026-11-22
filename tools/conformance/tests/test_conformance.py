"""tools/conformance 的自测（python -m unittest discover -s tools/conformance/tests）。

原则：每个检查都要能失败。因此除了"正确桩全绿"，还断言两个故意写错的桩必须报失败。
"""
import io
import json
import os
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)
ROOT = os.path.abspath(os.path.join(TOOL, "..", ".."))
SAMPLES = os.path.join(ROOT, "spec", "conformance", "samples")
VECTORS = os.path.join(ROOT, "spec", "conformance", "redaction-vectors.jsonl")

sys.path.insert(0, TOOL)

import compare  # noqa: E402
import gen_samples  # noqa: E402
import redactor  # noqa: E402
import runner  # noqa: E402

EXPECTED_IDS = 78


def py(script, *args):
    return subprocess.run([sys.executable, os.path.join(TOOL, script)] + list(args),
                          capture_output=True, text=True, encoding="utf-8", cwd=ROOT)


class TestSamples(unittest.TestCase):
    def test_all_78_dirs_present_with_required_files(self):
        dirs = sorted(d for d in os.listdir(SAMPLES)
                      if os.path.isdir(os.path.join(SAMPLES, d)))
        self.assertEqual(len(dirs), EXPECTED_IDS, dirs)
        for d in dirs:
            meta = json.load(io.open(os.path.join(SAMPLES, d, "meta.json"), encoding="utf-8"))
            self.assertEqual(meta["case"], d)
            self.assertIn("chunk_pattern", meta)
            self.assertTrue(
                os.path.exists(os.path.join(SAMPLES, d, "expected.jsonl"))
                or meta.get("expect_error"),
                "%s 既无 expected.jsonl 也无 expect_error" % d)
            if meta.get("layer") != "import":
                self.assertTrue(os.path.exists(os.path.join(SAMPLES, d, "stream.sse")))

    def test_chunk_pattern_grammar_is_closed_set(self):
        import re
        pat = re.compile(r"^(whole|bytes:\d+|fixed:\d+|random:\d+)$")
        for d in os.listdir(SAMPLES):
            p = os.path.join(SAMPLES, d, "meta.json")
            if not os.path.isfile(p):
                continue
            meta = json.load(io.open(p, encoding="utf-8"))
            self.assertTrue(pat.match(meta["chunk_pattern"]), meta["chunk_pattern"])

    def test_tree_hash_matches_recorded_baseline(self):
        baseline = os.path.join(HERE, "samples.sha256")
        self.assertTrue(os.path.exists(baseline), "缺 samples.sha256 基线")
        want = io.open(baseline, encoding="utf-8").read().strip()
        got, _ = gen_samples.tree_hash(SAMPLES)
        self.assertEqual(got, want)


class TestRedaction(unittest.TestCase):
    def test_vectors_exist_and_have_two_fields(self):
        rows = [json.loads(l) for l in io.open(VECTORS, encoding="utf-8") if l.strip()]
        self.assertGreaterEqual(len(rows), 40)
        for r in rows:
            self.assertIn("input", r)
            self.assertIn("expected", r)

    def test_redactor_matches_every_vector(self):
        rows = [json.loads(l) for l in io.open(VECTORS, encoding="utf-8") if l.strip()]
        for r in rows:
            self.assertEqual(redactor.redact(r["input"]), r["expected"], r["id"])

    def test_truncation_is_utf8_safe(self):
        out = redactor.redact("中" * 1000)
        self.assertLessEqual(len(out.encode("utf-8")), 512)
        out.encode("utf-8").decode("utf-8")  # 不抛异常即未切坏字符

    def test_header_with_key_is_fully_redacted(self):
        got = redactor.redact("Authorization: Bearer sk-abcdefghijklmnopqrstuvwx")
        self.assertEqual(got, "Authorization: [redacted]")


class TestRunner(unittest.TestCase):
    def test_stub_passes_subset(self):
        out = os.path.join(ROOT, "build", "cf-test", "sub")
        r = py("runner.py", "--impl", "stub", "--cases",
               "FRAME-001,FRAME-008,OAI-003,ANT-009,GEM-002,ERR-005", "--out", out)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("pass=6", r.stdout)

    def test_empty_delta_stub_fails(self):
        r = py("runner.py", "--impl", "fixtures/stub_emits_empty_delta.py",
               "--cases", "FRAME-011", "--out", os.path.join(ROOT, "build", "cf-test", "neg1"))
        self.assertEqual(r.returncode, 1, r.stdout)
        self.assertIn("FRAME-011", r.stdout)
        self.assertIn("fail=1", r.stdout)

    def test_duplicate_done_stub_fails(self):
        r = py("runner.py", "--impl", "fixtures/stub_duplicate_done.py",
               "--cases", "FRAME-006", "--out", os.path.join(ROOT, "build", "cf-test", "neg2"))
        self.assertEqual(r.returncode, 1, r.stdout)
        self.assertIn("fail=1", r.stdout)

    def test_events_jsonl_is_canonical(self):
        out = os.path.join(ROOT, "build", "cf-test", "canon")
        py("runner.py", "--impl", "stub", "--cases", "OAI-001,FRAME-008",
           "--out", out)
        for case in ("OAI-001", "FRAME-008"):
            path = os.path.join(out, "stub", case, "events.jsonl")
            data = io.open(path, "rb").read()
            self.assertFalse(data.startswith(b"\xef\xbb\xbf"), "带 BOM")
            self.assertNotIn(b"\r", data)
            _, _, err = compare.read_events(path)
            self.assertIsNone(err, err)


class TestCompare(unittest.TestCase):
    def test_two_runs_of_same_impl_are_identical(self):
        a = os.path.join(ROOT, "build", "cf-test", "cmp-a")
        b = os.path.join(ROOT, "build", "cf-test", "cmp-b")
        py("runner.py", "--impl", "stub", "--cases", "OAI-001,OAI-009,ANT-001",
           "--out", a, "--impl-name", "a")
        py("runner.py", "--impl", "stub", "--cases", "OAI-001,OAI-009,ANT-001",
           "--out", b, "--impl-name", "b")
        r = py("compare.py", "--runs", os.path.join(a, "a"), os.path.join(b, "b"),
               "--cases", "OAI-001,OAI-009,ANT-001")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertIn("differ=0", r.stdout)

    def test_missing_assert_entry_fails(self):
        """exit.json 少了 meta 要求的断言条目 → 该用例必须判失败，不许静默通过。"""
        run = os.path.join(ROOT, "build", "cf-test", "no-assert")
        py("runner.py", "--impl", "stub", "--cases", "CAN-002", "--out", run)
        p = os.path.join(run, "stub", "CAN-002", "exit.json")
        obj = json.load(io.open(p, encoding="utf-8"))
        obj["asserts"] = []
        with io.open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(obj))
        r = py("compare.py", "--runs", os.path.join(run, "stub"))
        self.assertEqual(r.returncode, 1, r.stdout)
        self.assertIn("缺断言条目", r.stdout)


if __name__ == "__main__":
    unittest.main()
