"""许可证检查的负向自证：黑名单组件、非白名单运行期组件、灰区运行期组件都必须被判失败。"""
import copy
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from license_check import check  # noqa: E402

import json
import io

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

    def test_gpl_runtime_component_fails(self):
        failures, _w = check(POLICY, with_component(
            name="copyleft-lib", version="1.0.0", license="GPL-3.0-only", scope="runtime"))
        self.assertTrue(any("copyleft-lib" in f and "黑名单" in f for f in failures), failures)

    def test_agpl_test_component_still_fails(self):
        failures, _w = check(POLICY, with_component(
            name="agpl-tool", version="2.0.0", license="AGPL-3.0-only", scope="test"))
        self.assertTrue(any("agpl-tool" in f for f in failures), failures)

    def test_unknown_runtime_license_fails(self):
        failures, _w = check(POLICY, with_component(
            name="weird-lib", version="3.1.4", license="Beerware", scope="runtime"))
        self.assertTrue(any("weird-lib" in f and "白名单" in f for f in failures), failures)

    def test_gray_runtime_license_fails(self):
        failures, _w = check(POLICY, with_component(
            name="epl-runtime-lib", version="1.0.0", license="EPL-2.0", scope="runtime"))
        self.assertTrue(any("epl-runtime-lib" in f and "灰区" in f for f in failures), failures)

    def test_incomplete_entry_fails(self):
        failures, _w = check(POLICY, with_component(name="no-license-lib", version="1.0.0", scope="runtime"))
        self.assertTrue(any("no-license-lib" in f and "登记不完整" in f for f in failures), failures)


if __name__ == "__main__":
    unittest.main()
