"""记录包体基线：为空工程产物写下字节数、SHA-256 与文件数，作为后续 M 阶段的对比基线。"""
import argparse
import hashlib
import json
import os
import sys


def measure(path):
    if os.path.isfile(path):
        names = [path]
    else:
        names = sorted(
            os.path.join(root, name)
            for root, _dirs, files in os.walk(path)
            for name in files
        )
    total = sum(os.path.getsize(n) for n in names)
    digest = hashlib.sha256()
    for n in names:
        with open(n, "rb") as fh:
            for chunk in iter(lambda: fh.read(1 << 20), b""):
                digest.update(chunk)
    return {"bytes": total, "files": len(names), "sha256": digest.hexdigest()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--platform", required=True, choices=["android", "windows"])
    ap.add_argument("--path", required=True)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    result = measure(args.path)
    result.update({"platform": args.platform, "path": args.path, "milestone": "M0"})
    out = args.out or os.path.join("ci", "baselines", args.platform + ".json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=2, sort_keys=True)
        fh.write("\n")
    print("baseline %s: bytes=%d files=%d sha256=%s -> %s" % (
        args.platform, result["bytes"], result["files"], result["sha256"], out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
