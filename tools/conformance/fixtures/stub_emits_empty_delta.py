"""故意写错的桩实现（负向验证用）：漏掉 I6 —— 空文本 delta 也发射。

预期：FRAME-011 必须失败（"只有 2 个 delta"变成 5 个）。
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import stub_impl  # noqa: E402
from stub_impl import Adapter as _Base  # noqa: E402
from stub_impl import ConnectionReset, ReadTimeout  # noqa: E402,F401


class Adapter(_Base):
    def _frame_openai(self, event_name, data):
        s = (data or "").strip()
        if s and s != "[DONE]":
            try:
                obj = json.loads(s)
                delta = ((obj.get("choices") or [{}])[0] or {}).get("delta") or {}
                text = delta.get("content")
                if isinstance(text, str) and text == "":
                    yield self._mk("delta", text="")
            except ValueError:
                pass
        for ev in _Base._frame_openai(self, event_name, data):
            yield ev
