"""契约 DDL 与应用侧 schema 快照的逐列比对。

两侧都归一到同一形状：{表: {列: {type, notnull, pk, default}}}。
任意差异都以 "表名.列名" 开头输出，退出码 1。
"""
import argparse
import json
import sqlite3
import sys

from gen_baseline import snapshot


def diff(sql_text, snapshot_obj):
    ddl = snapshot(sql_text)["tables"]
    app = snapshot_obj["tables"]
    out = []
    for table in sorted(set(ddl) | set(app)):
        if table not in app:
            out.append("%s: 快照缺表（DDL 有，应用侧无）" % table)
            continue
        if table not in ddl:
            out.append("%s: 快照多表（应用侧有，DDL 无）" % table)
            continue
        dcols = ddl[table]["columns"]
        acols = app[table]["columns"]
        for col in sorted(set(dcols) | set(acols)):
            if col not in acols:
                out.append("%s.%s: 快照缺列" % (table, col))
                continue
            if col not in dcols:
                out.append("%s.%s: 快照多列" % (table, col))
                continue
            for key in ("type", "notnull", "pk", "default"):
                if dcols[col][key] != acols[col][key]:
                    out.append(
                        "%s.%s: %s 差异 DDL=%r 快照=%r"
                        % (table, col, key, dcols[col][key], acols[col][key])
                    )
    return out, ddl, app


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sql", required=True)
    ap.add_argument("--json", required=True)
    args = ap.parse_args()

    sql_text = open(args.sql, encoding="utf-8").read()
    snap = json.load(open(args.json, encoding="utf-8"))
    problems, ddl, app = diff(sql_text, snap)

    ncol = sum(len(t["columns"]) for t in ddl.values())
    for line in problems:
        print("DIFF " + line)
    if problems:
        print("=== schema-diff: %d 处差异（表数 DDL=%d 快照=%d，列数 DDL=%d）===" % (
            len(problems), len(ddl), len(app), ncol))
        return 1
    print("=== schema-diff: DIFF 0（表数 %d，列数 %d）===" % (len(ddl), ncol))
    return 0


if __name__ == "__main__":
    sys.exit(main())
