"""把 Room 导出的 schema JSON 转成 playbook 约定的快照格式（M1 起取代 gen_baseline.py）。

Room 的 `createSql` 是它真正执行的建表语句；把 22 张实体表的语句连同 FTS5 虚表
（`res/raw/schema_fts5.sql`，Room 声明不了虚表，由建库回调执行）一起灌进内存库，
再用与 gen_baseline.py 相同的 PRAGMA 取列，输出 `neochat-schema-snapshot/1`。

用法：
  python tools/schema-diff/room_export_to_snapshot.py \
    --room-json androidApp/app/schemas/com.neochat.app.data.NeoChatDatabase/1.json \
    --fts-sql androidApp/app/src/main/res/raw/schema_fts5.sql \
    --out androidApp/schema/room-v1.json
然后：
  python tools/schema-diff/diff_schema.py --sql spec/schema/v1.sql --json androidApp/schema/room-v1.json
"""
import argparse
import json
import sys

from gen_baseline import snapshot


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--room-json", required=True)
    ap.add_argument("--fts-sql", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    exported = json.load(open(args.room_json, encoding="utf-8"))
    entities = exported["database"]["entities"]
    statements = []
    for entity in entities:
        table = entity["tableName"]
        statements.append(entity["createSql"].replace("${TABLE_NAME}", table).rstrip().rstrip(";") + ";")
        statements.extend(
            index["createSql"].replace("${TABLE_NAME}", table).rstrip().rstrip(";") + ";"
            for index in entity.get("indices", [])
        )
    statements.append(open(args.fts_sql, encoding="utf-8").read().strip())
    snap = snapshot("\n".join(statements))

    with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(snap, fh, ensure_ascii=False, indent=2, sort_keys=True)
        fh.write("\n")

    ncol = sum(len(t["columns"]) for t in snap["tables"].values())
    print("wrote %s: rooms-entities=%d tables=%d columns=%d"
          % (args.out, len(entities), len(snap["tables"]), ncol))
    return 0


if __name__ == "__main__":
    sys.exit(main())
