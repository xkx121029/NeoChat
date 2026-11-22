"""依赖许可证检查：白名单 + 黑名单。

规则来源 docs/security/README.md：
  允许 MIT / Apache-2.0 / BSD-2 / BSD-3 / ISC / MPL-2.0 / Zlib
  禁止 GPL / AGPL / LGPL / SSPL 及任何"仅非商业"条款

判定口径：
  - 黑名单与"仅非商业"条款对所有作用域生效（含 test）；
  - 白名单只对随包分发的作用域生效（allowlist.json 的 enforce_allowlist_scopes），
    因为测试框架不进入安装包；不在白名单且不在黑名单的作用域外组件以 WARN 打印并要求 D6 确认。
"""
import argparse
import json
import sys

GRAY_PREFIXES = ("CC-BY-NC",)


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def check(policy, inventory):
    allow = set(policy["allow"])
    deny = set(policy["deny"])
    gray = set(policy.get("gray", []))
    enforced = set(policy["enforce_allowlist_scopes"])

    failures, warnings = [], []
    for c in inventory["components"]:
        name, scope, lic = c.get("name"), c.get("scope"), c.get("license")
        if not name or not lic or not scope:
            failures.append("%s: 登记不完整（name/version/license/scope 必填）" % (name or "<unnamed>"))
            continue
        if lic in deny or lic.upper().startswith("GPL") or any(g in lic for g in GRAY_PREFIXES):
            failures.append("%s %s: 黑名单许可证 %s（scope=%s）" % (name, c.get("version", ""), lic, scope))
            continue
        if lic in gray:
            if scope in enforced:
                failures.append("%s %s: 灰区许可证 %s（scope=%s）需 ADR 后方可通过" % (
                    name, c.get("version", ""), lic, scope))
            else:
                warnings.append("%s %s: 灰区许可证 %s（scope=%s 不随包分发），需 D6 确认" % (
                    name, c.get("version", ""), lic, scope))
            continue
        if scope in enforced and lic not in allow:
            failures.append("%s %s: 许可证 %s 不在白名单（scope=%s）" % (name, c.get("version", ""), lic, scope))
            continue
        if scope not in enforced and lic not in allow:
            warnings.append("%s %s: %s（scope=%s 不随包分发，黑名单已过）" % (name, c.get("version", ""), lic, scope))
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
    print("=== license: 组件 %d，黑名单/白名单违规 %d，待确认 %d ===" % (
        len(inventory["components"]), len(failures), len(warnings)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
