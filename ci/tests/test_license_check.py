"""许可证检查的负向自证：黑名单、非白名单运行期组件、灰区运行期组件、无许可声明都必须被判失败。

规则随 docs/security/LICENSE-POLICY.md（AGPL-3 项目口径）：GPL-3.0 可用（保留声明），
GPL-2.0-only / SSPL / BUSL 必须被拒。
"""
import copy
import io
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from license_check import check  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
POLICY = json.load(io.open(os.path.join(REPO, "ci", "licenses", "allowlist.json"), encoding="utf-8"))
INVENTORY = json.load(io.open(os.path.join(REPO, "ci", "licenses", "dependencies.json"), encoding="utf-8"))


def with_component(**fields):
    inv = copy.deepcopy(INVENTORY)
    inv["components"].append(fields)
    return inv


class LicenseCheckTest(unittest.TestCase):
    def test_real_inventory_passes(self):
        failures, _warn = check(POLICY, INVENTORY)
        self.assertEqual(failures, [])

    def test_gpl2_only_runtime_component_fails(self):
        failures, _w = check(POLICY, with_component(
            name="copyleft-lib", version="1.0.0", license="GPL-2.0-only", scope="runtime"))
        self.assertTrue(any("copyleft-lib" in f and "黑名单" in f for f in failures), failures)

    def test_sspl_test_component_still_fails(self):
        failures, _w = check(POLICY, with_component(
            name="sspl-tool", version="2.0.0", license="SSPL-1.0", scope="test"))
        self.assertTrue(any("sspl-tool" in f for f in failures), failures)

    def test_busl_runtime_component_fails(self):
        failures, _w = check(POLICY, with_component(
            name="busl-lib", version="0.9.0", license="BUSL-1.1", scope="runtime"))
        self.assertTrue(any("busl-lib" in f for f in failures), failures)

    def test_unknown_runtime_license_fails(self):
        failures, _w = check(POLICY, with_component(
            name="weird-lib", version="3.1.4", license="Beerware", scope="runtime"))
        self.assertTrue(any("weird-lib" in f and "白名单" in f for f in failures), failures)

    def test_gray_runtime_license_fails(self):
        failures, _w = check(POLICY, with_component(
            name="epl-runtime-lib", version="1.0.0", license="EPL-2.0", scope="runtime"))
        self.assertTrue(any("epl-runtime-lib" in f and "灰区" in f for f in failures), failures)

    def test_copyleft_with_notice_passes_with_warning(self):
        failures, warnings = check(POLICY, with_component(
            name="gpl3-lib", version="3.0.0", license="GPL-3.0-only", scope="runtime"))
        self.assertEqual(failures, [])
        self.assertTrue(any("gpl3-lib" in w and "copyleft" in w for w in warnings), warnings)

    def test_incomplete_entry_fails(self):
        failures, _w = check(POLICY, with_component(name="no-license-lib", version="1.0.0", scope="runtime"))
        self.assertTrue(any("no-license-lib" in f and "登记不完整" in f for f in failures), failures)


if __name__ == "__main__":
    unittest.main()
