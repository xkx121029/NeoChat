"""故意写错的桩实现（负向验证用）：终止事件发两次，且不校验 I2/I3。

预期：所有带 done 的用例失败（events.jsonl 多一行 done，seq 重复/超尾）。
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import stub_impl  # noqa: E402
from stub_impl import Adapter as _Base  # noqa: E402
from stub_impl import ConnectionReset, ReadTimeout  # noqa: E402,F401


class Adapter(_Base):
    def events(self):
        last = None
        for ev in _Base.events(self):
            if ev.get("t") in ("done", "failed"):
                last = ev
            yield ev
        if last is not None and last.get("t") == "done":
            yield {"t": "done", "seq": self.seq, "finish_reason": last["finish_reason"]}
            self.seq += 1
