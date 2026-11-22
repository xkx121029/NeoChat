"""从 v1.sql 生成归一化 schema 快照（M0 用途）。

M0 阶段没有 Room/EF 实体代码（零业务代码），因此应用侧 schema 快照由契约 DDL 生成并入库；
M1 起该文件改由 Room 导出的 schema JSON 转换生成，diff_schema.py 的比对接口不变。
"""
import argparse
import json
import sqlite3
import sys


def snapshot(sql_text):
    con = sqlite3.connect(":memory:")
    con.executescript(sql_text)
    tables = {}
    for (name,) in con.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
    ):
        columns = {}
        for _cid, col, ctype, notnull, dflt, pk in con.execute("PRAGMA table_info(%s)" % name):
            columns[col] = {
                "type": (ctype or "").upper(),
                "notnull": bool(notnull),
                "pk": bool(pk),
                "default": dflt,
            }
        tables[name] = {"columns": columns}
    con.close()
    return {"format": "neochat-schema-snapshot/1", "tables": tables}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sql", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    snap = snapshot(open(args.sql, encoding="utf-8").read())
    with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(snap, fh, ensure_ascii=False, indent=2, sort_keys=True)
        fh.write("\n")
    ncol = sum(len(t["columns"]) for t in snap["tables"].values())
    print("wrote %s: tables=%d columns=%d" % (args.out, len(snap["tables"]), ncol))
    return 0


if __name__ == "__main__":
    sys.exit(main())
