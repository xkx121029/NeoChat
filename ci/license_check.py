"""依赖许可证检查：白名单 / copyleft 白名单 / 黑名单。

规则来源 docs/security/LICENSE-POLICY.md（2026-11-22 起，本仓库自身为 AGPL-3.0-or-later）：
  可直接用：MIT · Apache-2.0 · BSD-2/3-Clause · ISC · Zlib · Unlicense · MPL-2.0
  可用需保留声明：LGPL-3.0 · GPL-3.0 · GPL-2.0-or-later · AGPL-3.0
  禁止：GPL-2.0-only（与 AGPL-3 不兼容）· SSPL · BUSL · CC-BY-NC 类 · 无许可声明

判定口径：
  - 黑名单、"仅非商业"条款、无许可声明对所有作用域生效（含 test）；
  - 白名单与 copyleft 白名单只对随包分发的作用域生效（allowlist.json 的 enforce_allowlist_scopes），
    测试框架不进入安装包；作用域外且不在任何白名单的组件以 WARN 打印并需人工确认。
"""
import argparse
import json
import sys

NON_COMMERCIAL_PREFIXES = ("CC-BY-NC", "Commons-Clause")


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def check(policy, inventory):
    allow = set(policy["allow"])
    copyleft = set(policy["copyleft_with_notice"])
    deny = set(policy["deny"])
    gray = set(policy.get("gray", []))
    enforced = set(policy["enforce_allowlist_scopes"])

    failures, warnings = [], []
    for c in inventory["components"]:
        name, scope, lic, version = c.get("name"), c.get("scope"), c.get("license"), c.get("version", "")
        if not name or not lic or not scope:
            failures.append("%s: 登记不完整（name/version/license/scope 必填）" % (name or "<unnamed>"))
            continue
        if lic in deny or (lic.upper().startswith("GPL") and lic.upper().endswith("2.0-ONLY")) \
                or any(lic.startswith(p) for p in NON_COMMERCIAL_PREFIXES):
            failures.append("%s %s: 黑名单许可证 %s（scope=%s）" % (name, version, lic, scope))
            continue
        if lic in gray:
            if scope in enforced:
                failures.append("%s %s: 灰区许可证 %s（scope=%s）需人工评估并记 ADR" % (name, version, lic, scope))
            else:
                warnings.append("%s %s: 灰区许可证 %s（scope=%s 不随包分发），需人工评估" % (name, version, lic, scope))
            continue
        if scope in enforced and lic not in allow and lic not in copyleft:
            failures.append("%s %s: 许可证 %s 不在白名单（scope=%s）" % (name, version, lic, scope))
            continue
        if scope not in enforced and lic not in allow and lic not in copyleft:
            warnings.append("%s %s: %s（scope=%s 不随包分发，黑名单已过）" % (name, version, lic, scope))
        if lic in copyleft:
            warnings.append("%s %s: copyleft 许可证 %s，需保留声明并记入 THIRD-PARTY" % (name, version, lic))
    return failures, warnings


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--policy", default="ci/licenses/allowlist.json")
    ap.add_argument("--inventory", default="ci/licenses/dependencies.json")
    args = ap.parse_args()

    policy = load(args.policy)
    inventory = load(args.inventory)
    failures, warnings = check(policy, inventory)

    for w in warnings:
        print("WARN " + w)
    for f in failures:
        print("FAIL " + f)
    print("=== license: 组件 %d，违规 %d，需确认 %d ===" % (
        len(inventory["components"]), len(failures), len(warnings)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
