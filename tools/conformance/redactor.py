"""errors.md §7 的脱敏实现（桩实现 + conformance 共用；双端须各有自己的实现）。

管线顺序固定（先结构字段、再模式、再邮箱/路径、最后截断），顺序不同就产出
不同的 `raw`，而 `raw` 进入 `failed.error` 字节比对。`tools/conformance/runner.py
--verify-redaction spec/conformance/redaction-vectors.jsonl` 用共享向量验收本实现。
"""
import re

RAW_LIMIT = 512
REDACTED = "[redacted]"

HEADER_NAMES = ("authorization", "x-api-key", "api-key", "cookie", "set-cookie")
QUERY_NAMES = ("key", "api_key", "access_token")

# 1) 结构字段：`Name: value` 行 与 `"name": "value"` JSON 成员
_HEADER_LINE = re.compile(
    r"^(?P<pre>[ \t]*(?:%s)[ \t]*:[ \t]*)(?P<val>[^\r\n]*)$"
    % "|".join(re.escape(n) for n in HEADER_NAMES),
    re.IGNORECASE | re.MULTILINE)
_HEADER_JSON = re.compile(
    r"(?P<pre>\"(?:%s)\"\s*:\s*\")(?P<val>(?:[^\"\\]|\\.)*)(?P<post>\")"
    % "|".join(re.escape(n) for n in HEADER_NAMES),
    re.IGNORECASE)
_QUERY = re.compile(
    r"(?P<pre>[?&](?:%s)=)(?P<val>[^&#\s\"']*)"
    % "|".join(re.escape(n) for n in QUERY_NAMES),
    re.IGNORECASE)

# 2) provider key 形态：长的先匹配（sk-ant- 含在 sk- 前缀里）
KEY_PATTERNS = [
    re.compile(r"sk-ant-[A-Za-z0-9_-]{16,}"),
    re.compile(r"AIza[0-9A-Za-z_-]{20,}"),
    re.compile(r"gsk_[A-Za-z0-9]{20,}"),
    re.compile(r"xai-[A-Za-z0-9]{16,}"),
    re.compile(r"sk-[A-Za-z0-9_-]{16,}"),
]

# 3) 邮箱与本地路径
_EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")
_POSIX_HOME = re.compile(r"(?<![A-Za-z0-9])/(?:home|Users)/([A-Za-z0-9._-]+)")
_WIN_HOME = re.compile(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/]+[Uu]sers[\\/]+([^\\/\s\"']+)")


def _mask_key(m):
    s = m.group(0)
    return s[:4] + "\u2026" + s[-4:]


def _mask_email(m):
    s = m.group(0)
    local, domain = s.split("@", 1)
    return local[0] + "***@" + domain


def _truncate_utf8(s, limit=RAW_LIMIT):
    b = s.encode("utf-8")
    if len(b) <= limit:
        return s
    b = b[:limit]
    while b:
        try:
            return b.decode("utf-8")
        except UnicodeDecodeError:
            b = b[:-1]
    return ""


def redact(s):
    if not isinstance(s, str):
        s = str(s)
    # 1) 结构字段整体替换（值里可能就是整段 key，必须先整体干掉）
    s = _HEADER_LINE.sub(lambda m: m.group("pre") + REDACTED, s)
    s = _HEADER_JSON.sub(lambda m: m.group("pre") + REDACTED + m.group("post"), s)
    s = _QUERY.sub(lambda m: m.group("pre") + REDACTED, s)
    # 2) 残余的 key 形态串：首 4 + … + 尾 4
    for pat in KEY_PATTERNS:
        s = pat.sub(_mask_key, s)
    # 3) 邮箱与本地路径中的用户名
    s = _EMAIL.sub(_mask_email, s)
    s = _WIN_HOME.sub("~", s)
    s = _POSIX_HOME.sub("~", s)
    # 4) 最后截断，按 UTF-8 边界
    return _truncate_utf8(s)


def has_key_shape(s):
    """诊断用：截断后是否还残留一个完整 key 形态串（不应用于判定通过）。"""
    return any(p.search(s) for p in KEY_PATTERNS)
