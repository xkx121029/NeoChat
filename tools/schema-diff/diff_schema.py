"""契约 DDL 与应用侧 schema 快照的逐列比对。

两侧都归一到同一形状：{表: {列: {type, notnull, pk, default}}}。
任意差异都以 "表名.列名" 开头输出，退出码 1。
"""
import argparse
import json
import sqlite3
import sys

from gen_baseline import snapshot


def pk_as_notnull(tables):
    """SQLite 的 PRAGMA table_info 对非 INTEGER 的 PRIMARY KEY 列报 notnull=0，而 Room 建表一律写
    `NOT NULL PRIMARY KEY`。主键不收 NULL 是双方共同语义，这里把两侧主键列都按 NOT NULL 计；
    只对齐这一条语义，其余差异照旧逐条报出。"""
    for table in tables.values():
        for column in table["columns"].values():
            if column["pk"]:
                column["notnull"] = True
    return tables


def diff(sql_text, snapshot_obj, pk_notnull=False):
    ddl = snapshot(sql_text)["tables"]
    app = json.loads(json.dumps(snapshot_obj))["tables"]
    if pk_notnull:
        pk_as_notnull(ddl)
        pk_as_notnull(app)
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
    ap.add_argument("--pk-notnull", action="store_true", help="主键列两侧都按 NOT NULL 计")
    args = ap.parse_args()

    sql_text = open(args.sql, encoding="utf-8").read()
    snap = json.load(open(args.json, encoding="utf-8"))
    if args.pk_notnull:
        print("=== 归一化：主键列按 NOT NULL 计（SQLite PRAGMA 对非 INTEGER 主键报 0）===")
    problems, ddl, app = diff(sql_text, snap, pk_notnull=args.pk_notnull)

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
