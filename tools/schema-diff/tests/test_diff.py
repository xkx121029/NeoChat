"""schema-diff 的负向/正向自证：改动任一列都必须被检出并报出 表名.列名。"""
import copy
import io
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from gen_baseline import snapshot  # noqa: E402
from diff_schema import diff  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SQL = io.open(os.path.join(REPO, "spec", "schema", "v1.sql"), encoding="utf-8").read()
BASE = json.load(io.open(os.path.join(REPO, "androidApp", "schema", "schema-v1.json"), encoding="utf-8"))


def mutated(fn):
    snap = copy.deepcopy(BASE)
    fn(snap)
    return snap


class DiffTest(unittest.TestCase):
    def test_real_snapshot_has_zero_diff(self):
        problems, _ddl, _app = diff(SQL, BASE)
        self.assertEqual(problems, [])

    def test_column_type_change_is_reported_with_table_and_column(self):
        def change(snap):
            snap["tables"]["message"]["columns"]["role"]["type"] = "INTEGER"

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertTrue(problems, "改列类型未被检出")
        self.assertTrue(any(p.startswith("message.role: type") for p in problems), problems)

    def test_column_nullability_change_is_reported(self):
        def change(snap):
            snap["tables"]["provider"]["columns"]["billing_url"]["notnull"] = True

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertTrue(any(p.startswith("provider.billing_url: notnull") for p in problems), problems)

    def test_column_default_change_is_reported(self):
        def change(snap):
            snap["tables"]["provider"]["columns"]["concurrency_limit"]["default"] = "8"

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertTrue(any(p.startswith("provider.concurrency_limit: default") for p in problems), problems)

    def test_dropped_column_is_reported(self):
        def change(snap):
            snap["tables"]["message"]["columns"].pop("role")

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertEqual(len(problems), 1)
        self.assertTrue(problems[0].startswith("message.role:"), problems)

    def test_extra_column_is_reported(self):
        def change(snap):
            snap["tables"]["message"]["columns"]["selftest_col"] = {
                "type": "INTEGER", "notnull": False, "pk": False, "default": None}

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertTrue(any(p.startswith("message.selftest_col:") for p in problems), problems)

    def test_dropped_table_is_reported(self):
        def change(snap):
            snap["tables"].pop("tombstone")

        problems, _d, _a = diff(SQL, mutated(change))
        self.assertIn("tombstone: 快照缺表（DDL 有，应用侧无）", problems)

    def test_generated_snapshot_matches_committed_snapshot(self):
        self.assertEqual(snapshot(SQL), BASE, "schema-v1.json 与 v1.sql 不同步，请重跑 gen_baseline.py")


if __name__ == "__main__":
    unittest.main()
