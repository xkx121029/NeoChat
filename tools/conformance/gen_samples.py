"""按 spec/conformance/CASES.md v1.0.0 生成 conformance 样本。

    python tools/conformance/gen_samples.py            # 写 samples/ + redaction-vectors.jsonl
    python tools/conformance/gen_samples.py --hash      # 只打印目录树 sha256
    python tools/conformance/gen_samples.py --manifest  # 打印 ID + 文件数清单

确定性：固定 seed、固定 JSON 键序、gzip mtime=0。两次运行输出字节级相同。

样本目录之外的东西一律不碰：不读不写 spec/*.md，不碰 androidApp / windowsApp。
"""
import argparse
import gzip
import hashlib
import io
import json
import os
import random
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
try:  # Windows 控制台默认 cp936，会把中文输出打成乱码
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)

import errors_model  # noqa: E402
from redactor import redact  # noqa: E402

SAMPLES = os.path.join(ROOT, "spec", "conformance", "samples")
VECTORS = os.path.join(ROOT, "spec", "conformance", "redaction-vectors.jsonl")

PROVIDERS = {
    "openai": {"name": "openai", "model": "gpt-4o-mini"},
    "openai_compatible": {"name": "openai_compatible", "model": "deepseek-chat"},
    "openrouter": {"name": "openrouter", "model": "openai/gpt-4o-mini"},
    "responses": {"name": "openai", "model": "gpt-4.1"},
    "anthropic": {"name": "anthropic", "model": "claude-sonnet-4"},
    "gemini": {"name": "gemini", "model": "gemini-2.0-flash"},
}
SSE = "text/event-stream"
JSONCT = "application/json"

DONE_MARK = "data: [DONE]\n\n"


def dumps(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def jline(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


# ---------------------------------------------------------------- 事件声明
def D(text):
    return ("delta", {"text": text})


def RD(text):
    return ("reasoning_delta", {"text": text})


def TCD(index, **kw):
    return ("tool_call_delta", dict(index=index, **kw))


def TCE(index):
    return ("tool_call_end", {"index": index})


def U(inp, out, cached=0, total=None, reasoning=None, cost=None):
    d = {"input": inp, "output": out, "cached": cached,
         "total": inp + out if total is None else total, "estimated": False}
    if reasoning:
        d["reasoning"] = reasoning
    if cost is not None:
        d["provider_cost"] = {"amount": cost, "currency": "USD"}
    return ("usage", d)


def FIN(fr):
    return ("done", {"finish_reason": fr})


class Case(object):
    def __init__(self, cid, protocol="openai", layer="L2", status=200,
                 content_type=SSE, chunk="whole", **meta):
        self.cid = cid
        self.buf = bytearray()
        self.expected = []
        self.seq = 0
        self.extra = {}
        self.meta = {
            "case": cid,
            "cases_version": "1.0.0",
            "layer": layer,
            "protocol": protocol,
            "http_status": status,
            "http_headers": {"content-type": content_type},
            "provider": dict(PROVIDERS[protocol]),
            "chunk_pattern": chunk,
            "stream_file": "stream.sse",
        }
        self.meta.update({k: v for k, v in meta.items() if v is not None})

    # --- 样本字节 + 期望事件（声明式：两者都由本函数显式给出） ---
    def put(self, data, events=()):
        self.buf += data.encode("utf-8") if isinstance(data, str) else data
        for t, fields in events:
            ev = {"t": t, "seq": self.seq}
            self.seq += 1
            ev.update(fields)
            self.expected.append(ev)
        return self

    def fail(self, code, **kw):
        return ("failed", {"error": errors_model.make(code, self.meta, **kw)})

    def file(self, name, obj):
        self.extra[name] = (json.dumps(obj, ensure_ascii=False, indent=2, sort_keys=True)
                            + "\n").encode("utf-8")

    def write(self):
        d = os.path.join(SAMPLES, self.cid)
        os.makedirs(d, exist_ok=True)
        stream = os.path.join(d, "stream.sse")
        if self.buf or "stream_file" in self.meta:
            with open(stream, "wb") as f:
                f.write(bytes(self.buf))
        for name, data in self.extra.items():
            with open(os.path.join(d, name), "wb") as f:
                f.write(data)
        with open(os.path.join(d, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(self.meta, ensure_ascii=False, indent=2, sort_keys=True) + "\n")
        with open(os.path.join(d, "expected.jsonl"), "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(dumps(e) + "\n" for e in self.expected))
        return d


# ---------------------------------------------------------------- 线格式构造
def oai_chunk(delta=None, finish=None, index=0, usage=None, choices=None):
    if choices is None:
        ch = {"index": index, "delta": delta or {}}
        if finish is not None:
            ch["finish_reason"] = finish
        choices = [ch]
    obj = {"id": "chatcmpl-c0", "object": "chat.completion.chunk", "created": 1700000000,
           "model": "gpt-4o-mini", "choices": choices}
    if usage is not None:
        obj["usage"] = usage
    return "data: " + jline(obj) + "\n\n"


def oai_tc(index, id=None, name=None, args=None):
    tc = {"index": index}
    if id is not None:
        tc["id"] = id
        tc["type"] = "function"
    fn = {}
    if name is not None:
        fn["name"] = name
    if args is not None:
        fn["arguments"] = args
    tc["function"] = fn
    return tc


def sse(event, obj):
    return "event: %s\ndata: %s\n\n" % (event, jline(obj))


def data_only(obj):
    return "data: %s\n\n" % jline(obj)


def gzip_bytes(data):
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode="wb", mtime=0, compresslevel=6) as f:
        f.write(data)
    return buf.getvalue()


def in_string_flags(data):
    """标记每个字节位置是否处在 JSON 字符串字面量内部（用于证明切点落在字符串内）。"""
    flags = bytearray(len(data) + 1)
    inside = False
    esc = False
    for i, b in enumerate(data):
        flags[i] = 1 if inside else 0
        if inside:
            if esc:
                esc = False
            elif b == 0x5C:
                esc = True
            elif b == 0x22:
                inside = False
        elif b == 0x22:
            inside = True
    flags[len(data)] = 1 if inside else 0
    return flags


def plan_cuts(data, want=3, limit=64):
    """找一个 n，使 bytes:n 正好切出 want 段，且每个切点都落在 JSON 字符串内部。"""
    flags = in_string_flags(data)
    for n in range(1, limit):
        cuts = list(range(n, len(data), n))
        if len(cuts) != want - 1:
            continue
        if all(flags[p - 1] and flags[p] for p in cuts):
            return n, cuts
    return None, []


# ================================================================ 1. FRAME（13）
def frame_cases():
    out = []

    c = Case("FRAME-001", layer="L1", chunk="bytes:1",
             notes="逐字节喂入含中文/emoji/非 BMP 的流；事件序列须与 whole 切片完全相同")
    for t in ["你好，", "世界！", "🎉", "𝕏 中文", "结束了"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(DONE_MARK, [FIN("stop")])
    out.append(c)

    c = Case("FRAME-002", layer="L1", notes="单个 data 事件被切成 3 段，切点落在 JSON 字符串内部")
    body = oai_chunk({"content": "abcdefghij"})
    n, cuts = plan_cuts(body.encode("utf-8"), want=3)
    assert n, "FRAME-002 未找到能把 3 段都切在 JSON 字符串内部的 n"
    c.meta["chunk_pattern"] = "bytes:%d" % n
    c.meta["cut_offsets"] = cuts
    c.put(body, [D("abcdefghij")])
    c.put(DONE_MARK, [FIN("stop")])
    out.append(c)

    c = Case("FRAME-003", layer="L1", notes="每 5 行插入 : keep-alive 与 : OPENROUTER PROCESSING")
    lines = []
    texts = ["一", "二", "三", "四", "五", "六", "七", "八", "九", "十"]
    for t in texts:
        frame = oai_chunk({"content": t})
        lines.extend([frame.split("\n")[0], ""])
        c.put(oai_chunk({"content": t}), [D(t)])
        if len(lines) % 5 == 0:
            lines.extend([": keep-alive", ": OPENROUTER PROCESSING"])
    c.put(DONE_MARK, [FIN("stop")])
    c.buf = bytearray(("\n".join(lines) + "\n" + DONE_MARK).encode("utf-8"))
    out.append(c)

    c = Case("FRAME-004", layer="L1", notes="无 [DONE]，末 chunk 带 finish_reason:stop → EOF 宽容路径")
    for t in ["甲", "乙"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    out.append(c)

    c = Case("FRAME-005", layer="L1", notes="[DONE] 出现在内容中途，其后 3 个 chunk 不得产生事件（I2/I3）")
    c.put(oai_chunk({"content": "A"}), [D("A")])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    for t in ["B", "C", "D"]:
        c.put(oai_chunk({"content": t}))
    out.append(c)

    c = Case("FRAME-006", layer="L1", notes="[DONE] 重复 3 次 → 只有一个 done（I2）")
    c.put(oai_chunk({"content": "A"}), [D("A")])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK * 3)
    out.append(c)

    c = Case("FRAME-007", layer="L1", notes="混合 \\r\\n、\\r、\\n 行尾；payload 内不得残留 \\r")
    frames = [
        (oai_chunk({"content": "上"}), "\r\n\r\n"),
        (oai_chunk({"content": "下"}), "\r\r"),
        (oai_chunk({"content": "左"}), "\n\n"),
    ]
    for i, (frame, eol) in enumerate(frames):
        body = frame[:-2]  # 去掉构造时的 \n\n
        c.put(body + eol, [D(["上", "下", "左"][i])])
    c.put(oai_chunk({}, finish="stop").replace("\n\n", "\r\n\r\n"), [FIN("stop")])
    c.put(DONE_MARK.replace("\n\n", "\r\n\r\n"))
    out.append(c)

    c = Case("FRAME-008", layer="L1", notes="流首 BOM + 中段非法 UTF-8（0xFF 0xFE）→ BOM 丢弃、非法字节替换为 U+FFFD")
    c.put(b"\xef\xbb\xbf")
    c.put(b'data: {"id":"c","object":"chat.completion.chunk","choices":[{"index":0,'
          b'"delta":{"content":"\xe4\xbd\xa0')
    c.put(b"\xff\xfe")
    c.put(b'\xe5\xa5\xbd"}}]}\n\n', [D("你\ufffd\ufffd好")])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    big_prefix = ('data: {"id":"c","object":"chat.completion.chunk","choices":[{"index":0,'
                  '"delta":{"content":"')
    big_suffix = '"}}]}\n\n'
    size = 10 * 1024 * 1024
    pad = "a" * (size - len(big_prefix) - len(big_suffix))
    c = Case("FRAME-009", layer="L1",
             notes="单行 data 载荷 10 MiB（合法大 JSON，无内部换行）；单行上限不得低于 16 MiB")
    c.put(big_prefix + pad + big_suffix, [D(pad)])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    size20 = 20 * 1024 * 1024
    pad20 = "b" * (size20 - len(big_prefix) - len(big_suffix))
    c = Case("FRAME-010", layer="L1", expect_error="PARSE_EVENT_TOO_LARGE",
             expect={"socket_closed": True},
             notes="单事件载荷 20 MiB，超 16 MiB 上限 → failed(PARSE_EVENT_TOO_LARGE) 并关闭 socket")
    c.put(big_prefix + pad20 + big_suffix, [c.fail("PARSE_EVENT_TOO_LARGE")])
    out.append(c)

    c = Case("FRAME-011", layer="L2",
             notes="delta.content:\"\" 的空增量 3 次，其间夹 2 个正常增量 → 只有 2 个 delta（I6）")
    c.put(oai_chunk({"content": ""}))
    c.put(oai_chunk({"content": "一"}), [D("一")])
    c.put(oai_chunk({"content": ""}))
    c.put(oai_chunk({"content": "二"}), [D("二")])
    c.put(oai_chunk({"content": ""}))
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("FRAME-012", layer="L1", hold_connection_ms=5000,
             asserts={"socket": [{"expect": "stream_count_zero", "within_ms": 100}]},
             notes="[DONE] 后服务端继续占用连接 5s；done 后须立即满足 activeStreamCount()==0（I12）")
    for t in ["收", "尾"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    plain = (oai_chunk({"content": "gzip"}) + oai_chunk({"content": " 内容"})
             + oai_chunk({}, finish="stop") + DONE_MARK)
    c = Case("FRAME-013", layer="L1", content_type=SSE, content_encoding="gzip",
             notes="content_encoding:gzip；解压后事件序列须与非压缩版本逐字节相同")
    c.meta["content_encoding"] = "gzip"
    c.put(gzip_bytes(plain.encode("utf-8")),
          [D("gzip"), D(" 内容"), FIN("stop")])
    out.append(c)
    return out


# ================================================================ 2. OAI（12）
def oai_cases():
    out = []

    c = Case("OAI-001", layer="L2", notes="标准 5 个内容 chunk + [DONE]；seq 0..5")
    for i in range(5):
        t = "段%d" % (i + 1)
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-002", layer="L2",
             notes="stream_options.include_usage：末 chunk 带 usage → 唯一 usage 在 done 之前")
    for t in ["一", "二"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(oai_chunk({}, finish="stop",
                    usage={"prompt_tokens": 412, "completion_tokens": 37,
                           "total_tokens": 449,
                           "prompt_tokens_details": {"cached_tokens": 128}}),
          [U(412, 37, cached=128, total=449), FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-003", layer="L2",
             notes="tool_call arguments 切成 3 片，name 只在首片；tool_call_end 恰好 1 个（I5/I7）")
    c.put(oai_chunk({"tool_calls": [oai_tc(0, id="call_a1", name="web_search", args="")]}),
          [TCD(0, id="call_a1", name="web_search", args="")])
    c.put(oai_chunk({"tool_calls": [oai_tc(0, args='{"q":')]}), [TCD(0, args='{"q":')])
    c.put(oai_chunk({"tool_calls": [oai_tc(0, args='"天气"}')]}), [TCD(0, args='"天气"}')])
    c.put(oai_chunk({}, finish="tool_calls"), [TCE(0), FIN("tool_calls")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-004", layer="L2", notes="两个 tool_call（index 0/1）交错下发，各一个 tool_call_end")
    c.put(oai_chunk({"tool_calls": [oai_tc(0, id="call_a", name="alpha", args="")]}),
          [TCD(0, id="call_a", name="alpha", args="")])
    c.put(oai_chunk({"tool_calls": [oai_tc(1, id="call_b", name="beta", args="")]}),
          [TCD(1, id="call_b", name="beta", args="")])
    c.put(oai_chunk({"tool_calls": [oai_tc(0, args='{"x":1')]}), [TCD(0, args='{"x":1')])
    c.put(oai_chunk({"tool_calls": [oai_tc(1, args='{"y":2')]}), [TCD(1, args='{"y":2')])
    c.put(oai_chunk({}, finish="tool_calls"),
          [TCE(0), TCE(1), FIN("tool_calls")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-005", layer="L2", notes="同一 delta 同时含 content 与 reasoning_content：delta 在前，两通道独立")
    c.put(oai_chunk({"content": "正文", "reasoning_content": "思考"}),
          [D("正文"), RD("思考")])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-006", layer="L2", notes="finish_reason:length")
    c.put(oai_chunk({"content": "截断"}))
    c.put(oai_chunk({}, finish="length"), [D("截断"), FIN("length")])
    c.put(DONE_MARK)
    out.append(c)

    body = '{"error":{"message":"You exceeded your current quota","code":"insufficient_quota"}}'
    c = Case("OAI-007", layer="L2", status=200, content_type=JSONCT,
             expect_error="AUTH_INSUFFICIENT_QUOTA",
             notes="HTTP 200 + application/json 错误体 → failed；raw 脱敏后 ≤512 字节")
    c.put(body, [("failed", {"error": errors_model.from_http(
        c.meta, 200, body, c.meta["http_headers"])})])
    out.append(c)

    c = Case("OAI-008", layer="L2", notes="finish_reason:content_filter 且此前有内容 → done{content_filter}")
    c.put(oai_chunk({"content": "前文"}))
    c.put(oai_chunk({}, finish="content_filter"), [D("前文"), FIN("content_filter")])
    c.put(DONE_MARK)
    out.append(c)

    dup = {"content": "同一片段"}
    c = Case("OAI-009", layer="L2", duplicate=True,
             notes="同一内容 chunk 字节级重发 2 次且 meta.duplicate=true → 恰好 1 个 delta，事件总数 2")
    c.put(oai_chunk(dup), [D("同一片段")])
    c.put(oai_chunk(dup))
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-010", layer="L2", notes="3 个 chunk 顺序颠倒：按到达顺序发事件，seq 仍严格 +1（I1）")
    for t in ["三", "一", "二"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-011", layer="L2",
             notes="单帧同时含 content 与两个 tool_calls：delta → tcd[0] → tcd[1]，seq 连续（I8）")
    c.put(oai_chunk({"content": "先说一句",
                     "tool_calls": [oai_tc(0, id="call_0", name="a", args="{}"),
                                    oai_tc(1, id="call_1", name="b", args="{}")]}),
          [D("先说一句"), TCD(0, id="call_0", name="a", args="{}"),
           TCD(1, id="call_1", name="b", args="{}")])
    c.put(oai_chunk({}, finish="tool_calls"), [TCE(0), TCE(1), FIN("tool_calls")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("OAI-012", layer="L2", cumulative_stream=True,
             notes="前缀累积型端点：第 2、3 个 chunk 重发全文 → 只发增量（I11）")
    c.put(oai_chunk({"content": "你好"}), [D("你好")])
    c.put(oai_chunk({"content": "你好世界"}), [D("世界")])
    c.put(oai_chunk({"content": "你好世界！"}), [D("！")])
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)
    return out


# ================================================================ 3. RESP（5）
def resp_cases():
    out = []
    c = Case("RESP-001", protocol="responses", layer="L2",
             notes="response.created + 3× output_text.delta + completed；created 不产生事件")
    c.put(sse("response.created", {"type": "response.created",
                                   "response": {"id": "resp_1", "status": "in_progress"}}))
    for t in ["一", "二", "三"]:
        c.put(sse("response.output_text.delta", {"type": "response.output_text.delta",
                                                 "delta": t}), [D(t)])
    c.put(sse("response.completed", {"type": "response.completed",
                                     "response": {"id": "resp_1", "status": "completed"}}),
          [FIN("stop")])
    out.append(c)

    c = Case("RESP-002", protocol="responses", layer="L2",
             notes="reasoning_summary_text.delta ×2 与 output_text.delta ×2 交错，两通道分离")
    seq = [("reasoning_summary_text", "想"), ("output_text", "正"),
           ("reasoning_summary_text", "好"), ("output_text", "文")]
    for kind, t in seq:
        if kind == "reasoning_summary_text":
            c.put(sse("response.reasoning_summary_text.delta",
                      {"type": "response.reasoning_summary_text.delta", "delta": t}), [RD(t)])
        else:
            c.put(sse("response.output_text.delta",
                      {"type": "response.output_text.delta", "delta": t}), [D(t)])
    c.put(sse("response.completed", {"type": "response.completed", "response": {}}), [FIN("stop")])
    out.append(c)

    c = Case("RESP-003", protocol="responses", layer="L2",
             notes="function_call 的 args 分 4 片；tool_call_end 在 function_call_arguments.done 时发出")
    c.put(sse("response.output_item.added", {
        "type": "response.output_item.added",
        "item": {"type": "function_call", "id": "item_1", "call_id": "call_r1",
                 "name": "get_weather"}}),
        [TCD(0, id="call_r1", name="get_weather", args="")])
    for frag in ['{"ci', 'ty":', '"上海"', '}']:
        c.put(sse("response.function_call_arguments.delta",
                  {"type": "response.function_call_arguments.delta",
                   "item_id": "item_1", "delta": frag}), [TCD(0, args=frag)])
    c.put(sse("response.function_call_arguments.done",
              {"type": "response.function_call_arguments.done",
               "item_id": "item_1", "arguments": '{"city":"上海"}'}), [TCE(0)])
    c.put(sse("response.completed", {"type": "response.completed", "response": {}}),
          [FIN("tool_calls")])
    out.append(c)

    c = Case("RESP-004", protocol="responses", layer="L2",
             notes="completed 带完整 usage（含 cached 与 reasoning）；usage 位于 done 之前")
    c.put(sse("response.output_text.delta",
              {"type": "response.output_text.delta", "delta": "答"}), [D("答")])
    c.put(sse("response.completed", {"type": "response.completed", "response": {
        "id": "resp_4", "status": "completed",
        "usage": {"input_tokens": 412, "output_tokens": 37, "total_tokens": 449,
                  "input_tokens_details": {"cached_tokens": 128},
                  "output_tokens_details": {"reasoning_tokens": 12}}}}),
        [U(412, 37, cached=128, total=449, reasoning=12), FIN("stop")])
    out.append(c)

    c = Case("RESP-005", protocol="responses", layer="L2", expect_error="SERVER_ERROR",
             notes="response.failed（server_error）→ failed(SERVER_ERROR)")
    c.put(sse("response.output_text.delta",
              {"type": "response.output_text.delta", "delta": "先"}), [D("先")])
    err = {"type": "response.failed", "response": {
        "id": "resp_5", "status": "failed",
        "error": {"code": "server_error", "message": "The server had an error"}}}
    c.put(sse("response.failed", err),
          [("failed", {"error": errors_model.from_sse_error(
              c.meta, {"error": err["response"]["error"]})})])
    out.append(c)
    return out


# ================================================================ 4. ANT（9）
def anth_cases():
    out = []

    c = Case("ANT-001", protocol="anthropic", layer="L2",
             notes="message_start + 2 text_delta + message_delta + message_stop；usage 由两帧合并（I8）")
    c.put(sse("message_start", {"type": "message_start", "message": {
        "id": "msg_1", "type": "message", "role": "assistant",
        "usage": {"input_tokens": 412, "output_tokens": 1,
                  "cache_read_input_tokens": 0}}}))
    for t in ["你", "好"]:
        c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                          "delta": {"type": "text_delta", "text": t}}), [D(t)])
    c.put(sse("message_delta", {"type": "message_delta",
                                "delta": {"stop_reason": "end_turn"},
                                "usage": {"output_tokens": 37}}))
    c.put(sse("message_stop", {"type": "message_stop"}),
          [U(412, 37), FIN("stop")])
    out.append(c)

    c = Case("ANT-002", protocol="anthropic", layer="L2",
             notes="thinking_delta ×3 与 text_delta ×2 交错；思考与正文独立通道")
    c.put(sse("message_start", {"type": "message_start", "message": {
        "usage": {"input_tokens": 10, "output_tokens": 0}}}))
    plan = [("thinking", "想"), ("text", "一"), ("thinking", "了"), ("text", "二"), ("thinking", "下")]
    for kind, t in plan:
        if kind == "thinking":
            c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                              "delta": {"type": "thinking_delta", "thinking": t}}),
                  [RD(t)])
        else:
            c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 1,
                                              "delta": {"type": "text_delta", "text": t}}), [D(t)])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "end_turn"},
                                "usage": {"output_tokens": 7}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(10, 7), FIN("stop")])
    out.append(c)

    c = Case("ANT-003", protocol="anthropic", layer="L2",
             notes="tool_use block：content_block_start + input_json_delta ×3 + content_block_stop")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 20, "output_tokens": 0}}}))
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 1,
                                      "content_block": {"type": "tool_use", "id": "toolu_1",
                                                        "name": "search", "input": {}}}),
          [TCD(0, id="toolu_1", name="search", args="")])
    for frag in ['{"q"', ': "天气"', "}"]:
        c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 1,
                                          "delta": {"type": "input_json_delta",
                                                    "partial_json": frag}}), [TCD(0, args=frag)])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 1}), [TCE(0)])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "tool_use"},
                                "usage": {"output_tokens": 15}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(20, 15), FIN("tool_calls")])
    out.append(c)

    c = Case("ANT-004", protocol="anthropic", layer="L2", notes="每 2 个事件插入 ping → 无事件（I9）")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 5, "output_tokens": 0}}}))
    c.put(sse("ping", {"type": "ping"}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                      "delta": {"type": "text_delta", "text": "甲"}}), [D("甲")])
    c.put(sse("ping", {"type": "ping"}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                      "delta": {"type": "text_delta", "text": "乙"}}), [D("乙")])
    c.put(sse("ping", {"type": "ping"}))
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "end_turn"},
                                "usage": {"output_tokens": 2}}))
    c.put(sse("ping", {"type": "ping"}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(5, 2), FIN("stop")])
    out.append(c)

    err = {"type": "error", "error": {"type": "overloaded_error", "message": "Overloaded"}}
    c = Case("ANT-005", protocol="anthropic", layer="L2", expect_error="SERVER_OVERLOADED",
             persist={"truncated": True},
             notes="中途 error{overloaded_error}：保留此前的 2 个 delta 再 failed；落盘标记 truncated=true（L3 待验）")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 8, "output_tokens": 0}}}))
    for t in ["已生成", "的两段"]:
        c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                          "delta": {"type": "text_delta", "text": t}}), [D(t)])
    c.put(sse("error", err),
          [("failed", {"error": errors_model.from_sse_error(c.meta, err)})])
    out.append(c)

    c = Case("ANT-006", protocol="anthropic", layer="L2",
             notes="event: 行缺失（只按 data.type 分派）与 event:/data: 顺序颠倒，均须正确")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 6, "output_tokens": 0}}}))
    c.put(data_only({"type": "content_block_delta", "index": 0,
                     "delta": {"type": "text_delta", "text": "缺event"}}), [D("缺event")])
    reversed_frame = ('data: %s\n' % jline({"type": "content_block_delta", "index": 0,
                                            "delta": {"type": "text_delta", "text": "颠倒"}})
                      + "event: content_block_delta\n\n")
    c.put(reversed_frame, [D("颠倒")])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "end_turn"},
                                "usage": {"output_tokens": 4}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(6, 4), FIN("stop")])
    out.append(c)

    c = Case("ANT-007", protocol="anthropic", layer="L2",
             notes="message_delta.usage 只含 output_tokens，input 只在 message_start：合并为一条 usage")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 100, "output_tokens": 1}}}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                      "delta": {"type": "text_delta", "text": "并"}}), [D("并")])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "end_turn"},
                                "usage": {"output_tokens": 20}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(100, 20), FIN("stop")])
    out.append(c)

    c = Case("ANT-008", protocol="anthropic", layer="L2", notes="stop_reason:max_tokens → done{length}")
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                      "delta": {"type": "text_delta", "text": "长"}}), [D("长")])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "max_tokens"},
                                "usage": {"output_tokens": 9}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(0, 9), FIN("length")])
    out.append(c)

    c = Case("ANT-009", protocol="anthropic", layer="L2",
             notes="块序 0=thinking、1=text、2=tool_use(A)、3=text、4=tool_use(B)：eventIndex 为 0/1 而非 2/4")
    c.put(sse("message_start", {"type": "message_start",
                                "message": {"usage": {"input_tokens": 30, "output_tokens": 0}}}))
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 0,
                                      "content_block": {"type": "thinking"}}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 0,
                                      "delta": {"type": "thinking_delta", "thinking": "想"}}), [RD("想")])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 0}))
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 1,
                                      "content_block": {"type": "text"}}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 1,
                                      "delta": {"type": "text_delta", "text": "甲"}}), [D("甲")])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 1}))
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 2,
                                      "content_block": {"type": "tool_use", "id": "A",
                                                        "name": "alpha", "input": {}}}),
          [TCD(0, id="A", name="alpha", args="")])
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 2,
                                      "delta": {"type": "input_json_delta", "partial_json": '{"a":1}'}}),
          [TCD(0, args='{"a":1}')])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 2}), [TCE(0)])
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 3,
                                      "content_block": {"type": "text"}}))
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 3,
                                      "delta": {"type": "text_delta", "text": "乙"}}), [D("乙")])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 3}))
    c.put(sse("content_block_start", {"type": "content_block_start", "index": 4,
                                      "content_block": {"type": "tool_use", "id": "B",
                                                        "name": "beta", "input": {}}}),
          [TCD(1, id="B", name="beta", args="")])
    c.put(sse("content_block_delta", {"type": "content_block_delta", "index": 4,
                                      "delta": {"type": "input_json_delta", "partial_json": '{"b":2}'}}),
          [TCD(1, args='{"b":2}')])
    c.put(sse("content_block_stop", {"type": "content_block_stop", "index": 4}), [TCE(1)])
    c.put(sse("message_delta", {"type": "message_delta", "delta": {"stop_reason": "tool_use"},
                                "usage": {"output_tokens": 25}}))
    c.put(sse("message_stop", {"type": "message_stop"}), [U(30, 25), FIN("tool_calls")])
    out.append(c)
    return out


# ================================================================ 5. GEM（6）
def gem_cases():
    out = []

    def gem(parts, finish=None, usage=None, feedback=None):
        cand = {"content": {"parts": parts, "role": "model"}, "index": 0}
        if finish:
            cand["finishReason"] = finish
        obj = {"candidates": [cand]}
        if usage:
            obj["usageMetadata"] = usage
        if feedback:
            obj["promptFeedback"] = feedback
        return obj

    c = Case("GEM-001", protocol="gemini", layer="L2",
             notes="alt=sse 模式 3 个 chunk，末 chunk 带 usageMetadata")
    for t in ["一", "二", "三"]:
        c.put(data_only(gem([{"text": t}])), [D(t)])
    c.put(data_only(gem([], finish="STOP",
                        usage={"promptTokenCount": 12, "candidatesTokenCount": 5,
                               "totalTokenCount": 17, "cachedContentTokenCount": 4})),
          [U(12, 5, cached=4, total=17), FIN("stop")])
    out.append(c)

    c = Case("GEM-002", protocol="gemini", layer="L2", content_type=JSONCT, chunk="bytes:17",
             notes="数组模式 [{...},{...}]，按 17 字节切片：每个对象恰好产生对应事件，无粘连/截断")
    arr = [gem([{"text": "甲"}]), gem([{"text": "乙"}], finish="STOP")]
    c.put("[" + ",".join(jline(o) for o in arr) + "]",
          [D("甲"), D("乙"), FIN("stop")])
    out.append(c)

    c = Case("GEM-003", protocol="gemini", layer="L2", cumulative_stream=True,
             notes="累积文本模式（每次发全文，共 4 次）→ 只发增量（I11）")
    for full, inc in zip(["程", "程序", "程序员", "程序员！"], ["程", "序", "员", "！"]):
        c.put(data_only(gem([{"text": full}])), [D(inc)])
    c.put(data_only(gem([], finish="STOP")), [FIN("stop")])
    out.append(c)

    c = Case("GEM-004", protocol="gemini", layer="L2",
             notes="functionCall 一次性下发完整 args：1 个 tool_call_delta + 立即 1 个 tool_call_end")
    c.put(data_only(gem([{"functionCall": {"name": "lookup",
                                           "args": {"city": "北京", "n": 2}}}])),
          [TCD(0, id="call_0", name="lookup", args='{"city":"北京","n":2}'), TCE(0)])
    c.put(data_only(gem([], finish="STOP")), [FIN("stop")])
    out.append(c)

    c = Case("GEM-005", protocol="gemini", layer="L2", expect_error="CONTENT_FILTERED",
             notes="仅 promptFeedback.blockReason:SAFETY、无 candidates → failed(CONTENT_FILTERED)")
    obj = {"promptFeedback": {"blockReason": "SAFETY"}, "candidates": []}
    c.put(data_only(obj), [c.fail("CONTENT_FILTERED")])
    out.append(c)

    c = Case("GEM-006", protocol="gemini", layer="L2",
             notes="finishReason:SAFETY 且此前已有 2 个 delta → done{content_filter}，不是 failed")
    for t in ["部分", "内容"]:
        c.put(data_only(gem([{"text": t}])), [D(t)])
    c.put(data_only(gem([], finish="SAFETY")), [FIN("content_filter")])
    out.append(c)
    return out


# ================================================================ 6. ORT（4）
def ort_cases():
    out = []

    c = Case("ORT-001", protocol="openrouter", layer="L2",
             notes=": OPENROUTER PROCESSING 与内容交错 → 注释不产生事件（I9）")
    for i, t in enumerate(["云", "端"]):
        c.put(": OPENROUTER PROCESSING\n\n")
        c.put(oai_chunk({"content": t}), [D(t)])
    c.put(": OPENROUTER PROCESSING\n\n")
    c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("ORT-002", protocol="openrouter", layer="L2",
             notes="末 chunk usage 含 cost → usage.provider_cost={amount,currency}")
    c.put(oai_chunk({"content": "费"}), [D("费")])
    c.put(oai_chunk({}, finish="stop",
                    usage={"prompt_tokens": 20, "completion_tokens": 3, "total_tokens": 23,
                           "cost": 0.00231}),
          [U(20, 3, total=23, cost="0.00231"), FIN("stop")])
    c.put(DONE_MARK)
    out.append(c)

    c = Case("ORT-003", protocol="openrouter", layer="L2", expect_error="SERVER_ERROR",
             notes="data:{\"error\":{\"code\":502,...}} → failed(SERVER_ERROR)")
    err = {"error": {"code": 502, "message": "Provider returned error"}}
    c.put(data_only(err),
          [("failed", {"error": errors_model.from_sse_error(c.meta, err)})])
    out.append(c)

    c = Case("ORT-004", protocol="openrouter", layer="L2", status=429,
             http_headers={"content-type": JSONCT, "retry-after": "20",
                           "x-ratelimit-remaining-requests": "0"},
             expect_error="RATE_LIMITED",
             notes="429 + Retry-After:20 + x-ratelimit-remaining-requests:0 → "
                   "failed(RATE_LIMITED, retry_after_ms:20000)；头只进 trace，不进 StreamEvent")
    body = '{"error":{"message":"Rate limit exceeded","code":"rate_limit_exceeded"}}'
    c.meta["http_headers"] = {"content-type": JSONCT, "retry-after": "20",
                              "x-ratelimit-remaining-requests": "0"}
    c.put(body, [("failed", {"error": errors_model.from_http(
        c.meta, 429, body, c.meta["http_headers"])})])
    out.append(c)
    return out


# ================================================================ 7. ERR（6）
def err_cases():
    out = []

    def http_err(cid, status, body, code, headers=None, notes=""):
        hdrs = {"content-type": JSONCT}
        hdrs.update(headers or {})
        c = Case(cid, protocol="openai_compatible", layer="L2", status=status,
                 http_headers=hdrs, expect_error=code, notes=notes)
        c.meta["http_headers"] = hdrs
        c.put(body, [("failed", {"error": errors_model.from_http(
            c.meta, status, body, hdrs)})])
        return c

    out.append(http_err(
        "ERR-001", 401, '{"error":{"code":"invalid_api_key","message":"Incorrect API key provided"}}',
        "AUTH_INVALID_KEY", notes="HTTP 401 → AUTH_INVALID_KEY，retryable=false，action 含 edit_provider"))
    out.append(http_err(
        "ERR-002", 429, '{"error":{"code":"insufficient_quota","message":"quota exceeded"}}',
        "AUTH_INSUFFICIENT_QUOTA", notes="HTTP 429 + insufficient_quota → AUTH_INSUFFICIENT_QUOTA，action 含 top_up"))
    out.append(http_err(
        "ERR-003", 400, '{"error":{"code":"context_length_exceeded","message":"too long"}}',
        "CONTEXT_OVERFLOW", notes="HTTP 400 + context_length_exceeded → CONTEXT_OVERFLOW，action 含 reduce_context"))
    out.append(http_err(
        "ERR-004", 404, '{"error":{"message":"The model `gpt-9` does not exist"}}',
        "MODEL_NOT_FOUND", notes="HTTP 404 且 message 含 model → MODEL_NOT_FOUND"))

    c = Case("ERR-005", protocol="openai_compatible", layer="L1",
             expect_error="NET_CONNECTION_RESET",
             notes="传输中途 TCP RST（meta.abort_at_byte）：已生成内容保留，再 failed(NET_CONNECTION_RESET)")
    first = oai_chunk({"content": "已经收到的"})
    second = oai_chunk({"content": "这一片会断"})
    c.put(first, [D("已经收到的")])
    c.meta["abort_at_byte"] = len(first) + 20
    c.put(second + oai_chunk({}, finish="stop"), [c.fail("NET_CONNECTION_RESET")])
    out.append(c)

    chunks = [oai_chunk({"content": "等"}), oai_chunk({"content": "待"})]
    c = Case("ERR-006", protocol="openai_compatible", layer="L1",
             expect_error="NET_READ_TIMEOUT", simulate_idle_ms=500,
             test_config={"read_idle_ms": 500, "idle_ms": 181000},
             notes="长时间无字节（meta.idle_ms=181000 由测试配置加速为 500ms）→ failed(NET_READ_TIMEOUT)")
    c.put(chunks[0] + chunks[1], [D("等"), D("待"), c.fail("NET_READ_TIMEOUT")])
    out.append(c)
    return out


# ================================================================ 8. CAN（4）
def can_cases():
    out = []
    c = Case("CAN-001", protocol="openai_compatible", layer="L3", cancel_when={"after_events": 0},
             asserts={"socket": [{"expect": "stream_count_zero", "within_ms": 200}],
                      "db": [{"check": "message_status",
                              "args": {"message_id": "last", "status": "aborted"}}]},
             notes="连接建立后、首字节前取消 → 恰好一个 done{aborted_user}，无 failed（I10）")
    c.put(oai_chunk({"content": "不会到的"}), [FIN("aborted_user")])
    out.append(c)

    c = Case("CAN-002", protocol="openai_compatible", layer="L3", cancel_when={"after_events": 3},
             asserts={"db": [{"check": "message_part_truncated",
                              "args": {"part": "last", "value": True}}],
                      "socket": [{"expect": "stream_count_zero", "within_ms": 200}]},
             notes="收到 3 个 delta 后取消 → 3 delta + done{aborted_user}；DB 侧 truncated=1（ADR-003 §2.3）")
    for i, t in enumerate(["一", "二", "三", "四", "五"]):
        c.put(oai_chunk({"content": t}), [D(t)] if i < 3 else [])
    c.expected.append({"t": "done", "seq": c.seq, "finish_reason": "aborted_user"})
    c.seq += 1
    out.append(c)

    c = Case("CAN-003", protocol="openai_compatible", layer="L3",
             cancel_when={"after_events": 1}, next_request_max_delay_ms=10,
             fixtures={"conversation": "11111111-1111-7111-8111-111111111111"},
             asserts={"db": [{"check": "row_count",
                              "args": {"table": "message",
                                       "where": {"conversation_id": "$fixture.conversation",
                                                 "status": "streaming"},
                                       "count": 1}}]},
             notes="取消后 10ms 内发起新请求：旧流进 Terminal 后才发新请求；同一会话同时最多 1 条 streaming")
    for t in ["旧流"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.expected.append({"t": "done", "seq": c.seq, "finish_reason": "aborted_user"})
    c.seq += 1
    out.append(c)

    c = Case("CAN-004", protocol="openai_compatible", layer="L3", cancel_when={"after_events": 2},
             asserts={"socket": [{"expect": "stream_count_zero", "within_ms": 200}]},
             trace_asserts=["okhttp.EventListener.callEnd", "socketsHttpHandler.connectionCount"],
             notes="取消后检查连接池：归还包括归还连接池（I12）")
    for t in ["甲", "乙"]:
        c.put(oai_chunk({"content": t}), [D(t)])
    c.expected.append({"t": "done", "seq": c.seq, "finish_reason": "aborted_user"})
    c.seq += 1
    out.append(c)
    return out


# ================================================================ 9. RENDER（15）
def render_docs():
    docs = {}

    docs["RENDER-001"] = "\n".join(
        ["```python"] + ["print(%d)" % i for i in range(498)] + ["```"]) + "\n"

    docs["RENDER-002"] = (
        "- 一级 A\n"
        "  1. 二级 A-1\n"
        "     - 三级 A-1-a\n"
        "       1. 四级 A-1-a-1\n"
        "       2. 四级 A-1-a-2\n"
        "     - 三级 A-1-b\n"
        "  2. 二级 A-2\n"
        "- 一级 B\n"
        "  - 二级 B-1\n"
        "    - 三级 B-1-a\n"
        "      - 四级 B-1-a-1\n") 

    docs["RENDER-003"] = (
        "| 左对齐 | 居中 | 右对齐 |\n"
        "| :--- | :---: | ---: |\n"
        "| `a\\|b` | 转义竖线 | 3 |\n"
        "| `x` | 行内 `code` | 42 |\n") 

    docs["RENDER-004"] = "行内公式 $x^2$ 与块级：\n\n$$\n\\int_0^1 x^2 dx = \\frac{1}{3}\n$$\n"

    nodes = []
    for i in range(30):
        if i == 0:
            nodes.append("    A%d[开始]" % i)
        else:
            nodes.append("    A%d[节点%d] --> A%d" % (i, i, i - 1))
    docs["RENDER-005"] = "```mermaid\ngraph TD\n" + "\n".join(nodes) + "\n```\n"

    docs["RENDER-006"] = (
        "```mermaid\n"
        "sequenceDiagram\n"
        "    participant 客户端\n"
        "    participant 服务端\n"
        "    客户端->>服务端: 请求\n"
        "    alt 成功\n"
        "        服务端-->>客户端: 200\n"
        "    else 失败\n"
        "        服务端-->>客户端: 500\n"
        "    end\n"
        "```\n")

    docs["RENDER-007"] = "中英混排 Mixed 文本，含 emoji 😀🎉，非 BMP 字符 𝕏 与全角标点：，。！？\n"

    docs["RENDER-008"] = "مرحبا بالعالم hello world 混排 RTL 与 LTR 在同一段落内。\n"

    docs["RENDER-009"] = "正文引用脚注[^1]。\n\n[^1]: 脚注定义内容。\n"

    docs["RENDER-010"] = "- [x] 已完成项\n- [ ] 未完成项\n- 普通项\n"

    docs["RENDER-011"] = "原始 HTML：\n\n<script>alert(1)</script>\n\n<b>加粗会不会执行</b>\n"

    long_para = ("这是一段用于前缀稳定性测试的长文本。" * 5000)
    docs["RENDER-012"] = long_para[:100000] + "\n"

    docs["RENDER-013"] = "```\n| a | b |\n| - | - |\n| 1 | 2 |\n"

    docs["RENDER-014"] = "![a](x.png)\n\n见 ![b](y.png) 图\n"

    docs["RENDER-015"] = "看这个 [文档][d]\n\n[d]: https://x\n"
    return docs


def render_cases():
    out = []
    docs = render_docs()
    asserts = {
        "RENDER-001": ["code{closed:false} 直到围栏闭合后 code{closed:true}"],
        "RENDER-002": ["嵌套 list 结构与 model.json 一致（4 层，有序/无序混合）"],
        "RENDER-003": ["table{align:[left,center,right]}，`a\\|b` 不拆列"],
        "RENDER-004": ["inline math + block math 各就位"],
        "RENDER-005": ["mermaid{kind:flowchart}，30 节点，closed:true"],
        "RENDER-006": ["mermaid{kind:sequence}"],
        "RENDER-007": ["文本逐字符一致（代理对不得拆断）"],
        "RENDER-008": ["文本顺序不反转"],
        "RENDER-009": ["footnote_ref 与 footnote_def 配对"],
        "RENDER-010": ["list.items[].checked = true/false/null"],
        "RENDER-011": ["html 块，source 原样，不执行"],
        "RENDER-012": ["前缀稳定性 §4.5；perf: append-10 ≤ 2ms"],
        "RENDER-013": ["表格语法留在 code 块内，不产出 table"],
        "RENDER-014": ["段落只含图片 → Block image；夹在文字中 → paragraph{inlines:[text,image,text]}"],
        "RENDER-015": ["[文档][d] 原样为 text；[d]: https://x → unknown{kind:link_definition}"],
    }
    for i in range(1, 16):
        cid = "RENDER-%03d" % i
        doc = docs[cid]
        c = Case(cid, protocol="openai_compatible", layer="L1", layer_name="render",
                 render_asserts=asserts[cid],
                 notes="渲染层样本：markdown 文本按 OAI 文本增量下发；断言在 RenderModel 层（render-model.md）")
        c.meta["layer"] = "render"
        c.meta["layer_note"] = "需要双端 Markdown 解析器产出 RenderModel，协议桩不产出 blocks"
        lines = doc.splitlines(True)
        if cid == "RENDER-012":
            step = 500
        elif cid == "RENDER-001":
            step = 10
        else:
            step = 4
        for i0 in range(0, len(lines), step):
            piece = "".join(lines[i0:i0 + step])
            c.put(oai_chunk({"content": piece}), [D(piece)])
        if cid == "RENDER-012":
            for _ in range(100):
                piece = "0123456789"
                c.put(oai_chunk({"content": piece}), [D(piece)])
        c.put(oai_chunk({}, finish="stop"), [FIN("stop")])
        c.put(DONE_MARK)
        if cid == "RENDER-012":
            c.meta["asserts"] = {"perf": [{"metric": "append-10", "op": "lte", "value": 2}]}
            c.meta["perf_unit"] = "ms"
            c.meta["perf_method"] = "append-10-char"
        out.append(c)
    return out


# ================================================================ 10. EXPORT（4）
def export_bundle(conversations, messages, parts, attachments=None, usage=None):
    return {
        "format": "neochat-export",
        "format_version": 1,
        "exported_at": "2026-11-22T00:00:00Z",
        "app": {"name": "NeoChat", "version": "0.1.0", "schema_version": 1,
                "platform": "windows"},
        "options": {"scope": "all", "include_attachments": "none",
                    "include_secrets": False, "include_reasoning": True},
        "integrity": {"counts": {"conversations": len(conversations),
                                 "messages": len(messages),
                                 "message_parts": len(parts)},
                      "sha256": {}},
        "data": {"providers": [], "models": [], "conversations": conversations,
                 "messages": messages, "message_parts": parts,
                 "attachments": attachments or [], "usage_records": usage or []},
    }


def export_cases():
    out = []
    rng = random.Random(20261122)
    NS = uuid.UUID("00000000-0000-7000-8000-000000000001")

    convs, msgs, parts = [], [], []
    for i in range(1000):
        cid = str(uuid.uuid5(NS, "conv-%d" % i))
        ts = "2026-11-%02dT%02d:%02d:00Z" % (1 + i % 22, i % 24, i % 60)
        convs.append({"id": cid, "title": "会话 %d %s" % (i, rng.choice(
            ["架构", "排错", "设计", "复盘"])), "created_at": ts, "updated_at": ts,
            "sync_version": 1, "rev": 1, "title_auto": True, "pinned": i % 7 == 0,
            "archived": False, "message_count": 2})
        prev = None
        for j in range(2):
            mid = str(uuid.uuid5(NS, "msg-%d-%d" % (i, j)))
            msgs.append({"id": mid, "conversation_id": cid, "parent_id": prev,
                         "role": "user" if j == 0 else "assistant",
                         "sibling_index": 0, "status": "complete",
                         "finish_reason": None if j == 0 else "stop",
                         "truncated": False, "sync_version": 1, "rev": 1,
                         "created_at": ts, "updated_at": ts})
            parts.append({"id": str(uuid.uuid5(NS, "part-%d-%d" % (i, j))), "message_id": mid,
                          "part_index": 0, "kind": "text",
                          "body": "第 %d 轮第 %d 条消息正文。" % (i, j),
                          "created_at": ts, "updated_at": ts, "truncated": False})
            prev = mid

    c = Case("EXPORT-001", layer="import", layer_name="import",
             notes="1000 组随机会话（seed 固定）往返：两次导出 data 逐字段相同（§notes.2）")
    c.meta["layer"] = "import"
    c.meta["layer_note"] = "需要双端导入/导出器；桩实现不产出 StreamEvent"
    c.meta["payload_file"] = "payload.json"
    c.meta["payload_sha256"] = None
    c.file("payload.json", export_bundle(convs, msgs, parts))
    out.append(c)

    # EXPORT-002：分支树 + 6 条附件引用 + 用量记录 + 思考内容
    NS2 = uuid.UUID("00000000-0000-7000-8000-000000000002")
    cid = str(uuid.uuid5(NS2, "conv-branch"))
    root = str(uuid.uuid5(NS2, "msg-root"))
    a1 = str(uuid.uuid5(NS2, "msg-a1"))
    a2 = str(uuid.uuid5(NS2, "msg-a2"))
    b1 = str(uuid.uuid5(NS2, "msg-b1"))
    msgs2 = [
        {"id": root, "conversation_id": cid, "parent_id": None, "role": "user",
         "sibling_index": 0, "status": "complete", "truncated": False,
         "sync_version": 1, "rev": 1, "created_at": "2026-11-22T01:00:00Z",
         "updated_at": "2026-11-22T01:00:00Z"},
        {"id": a1, "conversation_id": cid, "parent_id": root, "role": "assistant",
         "sibling_index": 0, "status": "complete", "finish_reason": "stop",
         "truncated": False, "sync_version": 1, "rev": 1,
         "created_at": "2026-11-22T01:00:01Z", "updated_at": "2026-11-22T01:00:01Z"},
        {"id": a2, "conversation_id": cid, "parent_id": root, "role": "assistant",
         "sibling_index": 1, "status": "complete", "finish_reason": "stop",
         "truncated": False, "sync_version": 1, "rev": 1,
         "created_at": "2026-11-22T01:00:02Z", "updated_at": "2026-11-22T01:00:02Z"},
        {"id": b1, "conversation_id": cid, "parent_id": a1, "role": "user",
         "sibling_index": 0, "status": "complete", "truncated": False,
         "sync_version": 1, "rev": 1, "created_at": "2026-11-22T01:00:03Z",
         "updated_at": "2026-11-22T01:00:03Z"},
    ]
    parts2 = [
        {"id": str(uuid.uuid5(NS2, "p-thinking")), "message_id": a1, "part_index": 0,
         "kind": "reasoning", "body": "先分析分支树的结构。",
         "created_at": "2026-11-22T01:00:01Z", "updated_at": "2026-11-22T01:00:01Z",
         "truncated": False},
        {"id": str(uuid.uuid5(NS2, "p-text1")), "message_id": a1, "part_index": 1,
         "kind": "text", "body": "这是分支 A 的回答，引用附件 6 个。",
         "created_at": "2026-11-22T01:00:01Z", "updated_at": "2026-11-22T01:00:01Z",
         "truncated": False},
        {"id": str(uuid.uuid5(NS2, "p-text2")), "message_id": a2, "part_index": 0,
         "kind": "text", "body": "这是分支 B 的回答。",
         "created_at": "2026-11-22T01:00:02Z", "updated_at": "2026-11-22T01:00:02Z",
         "truncated": False},
    ]
    atts = [{"id": str(uuid.uuid5(NS2, "att-%d" % k)), "sha256": "%064x" % (k + 1),
             "size_bytes": 1024 * (k + 1), "mime": "image/png", "kind": "image",
             "local_path": "files/att-%d.png" % k, "ref_count": 1,
             "created_at": "2026-11-22T00:59:%02dZ" % k} for k in range(6)]
    usage = [{"id": str(uuid.uuid5(NS2, "u1")), "message_id": a1, "conversation_id": cid,
              "input_tokens": 412, "output_tokens": 37, "cached_tokens": 128,
              "reasoning_tokens": 12, "cost_estimate": "0.000412", "currency": "USD",
              "is_estimate": False, "created_at": "2026-11-22T01:00:01Z"}]
    c = Case("EXPORT-002", layer="import",
             notes="含分支树（root→a1→b1 与 root→a2 兄弟）、6 条附件引用、用量记录、思考内容")
    c.meta["layer"] = "import"
    c.meta["layer_note"] = "需要双端导入/导出器；桩实现不产出 StreamEvent"
    c.meta["payload_file"] = "payload.json"
    bundle2 = export_bundle(
        [{"id": cid, "title": "分支与附件", "created_at": "2026-11-22T01:00:00Z",
          "updated_at": "2026-11-22T01:00:03Z", "sync_version": 1, "rev": 1,
          "title_auto": False, "pinned": True, "archived": False, "message_count": 4,
          "active_leaf_message_id": b1}], msgs2, parts2, atts, usage)
    bundle2["data"]["message_attachments"] = [
        {"message_id": a1, "attachment_id": a["id"], "part_index": 1, "role_hint": "user"}
        for a in atts]
    c.file("payload.json", bundle2)
    out.append(c)

    # EXPORT-003：三种恶意文件
    c = Case("EXPORT-003", layer="import",
             notes="恶意文件三种：40 层嵌套 / local_path 路径穿越 / 单字段 30MB，均须拒绝并给 JSON Pointer")
    c.meta["layer"] = "import"
    c.meta["layer_note"] = "需要双端导入器；桩实现不产出 StreamEvent"
    c.meta["import_files"] = [
        {"file": "payload_nested.json", "expect_error": "IMPORT_MALFORMED",
         "json_pointer": "/data/conversations/0/settings_json",
         "reason": "嵌套超过上限"},
        {"file": "payload_traversal.json", "expect_error": "IMPORT_MALFORMED",
         "json_pointer": "/data/attachments/0/local_path",
         "reason": "local_path 含 ../ 路径穿越"},
        {"file": "payload_oversize.json", "expect_error": "IMPORT_TOO_LARGE",
         "json_pointer": "/data/message_parts/0/body",
         "reason": "单字段 30MB 超上限"},
    ]
    c.meta["asserts"] = {"db": [
        {"check": "row_count", "args": {"table": "conversation", "where": {}, "count": 0}},
        {"check": "row_count", "args": {"table": "message", "where": {}, "count": 0}}]}

    deep = "x"
    for _ in range(40):
        deep = {"k": deep}
    nested = export_bundle(
        [{"id": str(uuid.uuid5(NS2, "conv-nested")), "title": "嵌套", "created_at":
          "2026-11-22T00:00:00Z", "updated_at": "2026-11-22T00:00:00Z", "sync_version": 1,
          "rev": 1, "title_auto": True, "pinned": False, "archived": False,
          "message_count": 0, "settings_json": deep}], [], [])
    c.file("payload_nested.json", nested)

    trav = export_bundle(
        [], [], [],
        [{"id": str(uuid.uuid5(NS2, "att-trav")), "sha256": "%064d" % 7, "size_bytes": 12,
          "mime": "text/plain", "kind": "file", "local_path": "../../etc/passwd",
          "ref_count": 1, "created_at": "2026-11-22T00:00:00Z"}])
    c.file("payload_traversal.json", trav)

    oversize = export_bundle(
        [], [], [{"id": str(uuid.uuid5(NS2, "p-big")),
                  "message_id": str(uuid.uuid5(NS2, "msg-big")), "part_index": 0,
                  "kind": "text", "body": "z" * (30 * 1024 * 1024),
                  "created_at": "2026-11-22T00:00:00Z",
                  "updated_at": "2026-11-22T00:00:00Z", "truncated": False}])
    c.file("payload_oversize.json", oversize)
    out.append(c)

    c = Case("EXPORT-004", layer="import", expect_error="IMPORT_UNSUPPORTED_VERSION",
             notes="format_version:2 → IMPORT_UNSUPPORTED_VERSION，不尝试部分导入")
    c.meta["layer"] = "import"
    c.meta["layer_note"] = "需要双端导入器；桩实现不产出 StreamEvent"
    c.meta["payload_file"] = "payload.json"
    bad = export_bundle([], [], [])
    bad["format_version"] = 2
    c.file("payload.json", bad)
    out.append(c)
    return out


# ================================================================ 脱敏向量
def redaction_vectors():
    V = []

    def v(rid, rule, inp, exp):
        V.append({"id": rid, "rule": rule, "input": inp, "expected": exp})

    v("RED-001", "authorization-header",
      "Authorization: Bearer sk-abcdefghijklmnopqrstuvwxyz012345\n",
      "Authorization: [redacted]\n")
    v("RED-002", "authorization-header-contains-key",
      "authorization: Bearer sk-proj-abcdefghijklmnopqrstuvwx",
      "authorization: [redacted]")
    v("RED-003", "x-api-key-header", "x-api-key: abc123secretvalue", "x-api-key: [redacted]")
    v("RED-004", "api-key-header", "api-key: zon-9f8e7d6c5b4a", "api-key: [redacted]")
    v("RED-005", "cookie-header", "Cookie: session=abc; theme=dark", "Cookie: [redacted]")
    v("RED-006", "set-cookie-header", "Set-Cookie: sid=xyz; HttpOnly; Secure",
      "Set-Cookie: [redacted]")
    v("RED-007", "header-uppercase", "AUTHORIZATION: Bearer xyz", "AUTHORIZATION: [redacted]")
    v("RED-008", "header-with-tab-separator-preserved", "x-api-key:	secret",
      "x-api-key:	[redacted]")
    v("RED-009", "header-empty-value-separator-preserved", "x-api-key:",
      "x-api-key:[redacted]")
    v("RED-010", "header-in-header-block",
      "GET /v1/chat HTTP/1.1\nHost: api.example.com\nAuthorization: Bearer sk-zzzzzzzzzzzzzzzzzzzz\n",
      "GET /v1/chat HTTP/1.1\nHost: api.example.com\nAuthorization: [redacted]\n")
    v("RED-011", "two-headers",
      "x-api-key: aaa\nAuthorization: Bearer bbb\n",
      "x-api-key: [redacted]\nAuthorization: [redacted]\n")
    v("RED-012", "json-authorization-field",
      '{"authorization":"Bearer sk-abcdefghijklmnopqrstuvwx"}',
      '{"authorization":"[redacted]"}')
    v("RED-013", "query-key",
      "https://api.example.com/v1/chat?key=sk-abcdefghijklmnopqrst&stream=true",
      "https://api.example.com/v1/chat?key=[redacted]&stream=true")
    v("RED-014", "query-api-key", "https://x/v1?api_key=zzz&model=x",
      "https://x/v1?api_key=[redacted]&model=x")
    v("RED-015", "query-access-token", "https://x/v1?access_token=tok_123",
      "https://x/v1?access_token=[redacted]")
    v("RED-016", "query-key-last", "https://x/v1?stream=true&key=abc",
      "https://x/v1?stream=true&key=[redacted]")
    v("RED-017", "sk-key", "Incorrect API key provided: sk-abcdefghijklmnopqrst",
      "Incorrect API key provided: sk-a\u2026qrst")
    v("RED-018", "sk-ant-key", "sk-ant-api03-abcdefghijklmnopqrstuvwxyz",
      "sk-a\u2026wxyz")
    v("RED-019", "aiza-key", "AIzaSyD-abcdefghijklmnopqrstuvwxyz12345",
      "AIza\u20262345")
    v("RED-020", "gsk-key", "gsk_abcdefghijklmnopqrstuvwxyz", "gsk_\u2026wxyz")
    v("RED-021", "xai-key", "xai-abcdefghijklmnopqrstuvwx", "xai-\u2026uvwx")
    v("RED-022", "two-keys", "key1=sk-aaaaaaaaaaaaaaaa1234 key2=gsk_bbbbbbbbbbbbbbbbbbbb5678",
      "key1=sk-a\u20261234 key2=gsk_\u20265678")
    v("RED-023", "key-too-short", "sk-abcdefghijklmno", "sk-abcdefghijklmno")
    v("RED-024", "key-uppercase-prefix", "SK-abcdefghijklmnopqrst", "SK-abcdefghijklmnopqrst")
    v("RED-025", "aiza-too-short", "AIzaSyD-abcdefghijklmn", "AIzaSyD-abcdefghijklmn")
    v("RED-026", "email", "user@example.com", "u***@example.com")
    v("RED-027", "email-plus", "alice+test@mail.example.co", "a***@mail.example.co")
    v("RED-028", "email-in-sentence",
      "Contact support@neochat.app for details.", "Contact s***@neochat.app for details.")
    v("RED-029", "posix-home", "/home/alice/work/app.db", "~/work/app.db")
    v("RED-030", "macos-home", "/Users/alice/Library/Application Support/x", "~/Library/Application Support/x")
    v("RED-031", "windows-home", "C:\\Users\\alice\\AppData\\Roaming\\x.db",
      "~\\AppData\\Roaming\\x.db")
    v("RED-032", "windows-home-forward-slash", "D:/Users/bob/data", "~/data")
    v("RED-033", "windows-home-in-json", '{"path":"C:\\\\Users\\\\carol\\\\a.txt"}',
      '{"path":"~\\\\a.txt"}')
    v("RED-034", "exactly-512-ascii", "a" * 512, "a" * 512)
    v("RED-035", "513-ascii-truncated", "a" * 513, "a" * 512)
    v("RED-036", "truncate-multibyte-boundary", "a" * 511 + "中", "a" * 511)
    v("RED-037", "truncate-emoji-boundary", "a" * 510 + "😀", "a" * 510)
    v("RED-038", "redact-before-truncate-key-at-boundary",
      "a" * 500 + "sk-aaaaaaaaaaaaaaaaaaaa", "a" * 500 + "sk-a\u2026aaaa")
    v("RED-039", "redact-before-truncate-header-near-limit",
      "a" * 480 + "\nAuthorization: Bearer sk-bbbbbbbbbbbbbbbbbbbbbb\n",
      "a" * 480 + "\nAuthorization: [redacted]\n")
    v("RED-040", "no-false-positive-plain-text",
      "The sk- prefix alone and AIza are not keys.", "The sk- prefix alone and AIza are not keys.")
    v("RED-041", "error-body-json",
      '{"error":{"message":"Incorrect API key provided: sk-abcdefghijklmnopqrst",'
      '"code":"invalid_api_key"}}',
      '{"error":{"message":"Incorrect API key provided: sk-a\u2026qrst",'
      '"code":"invalid_api_key"}}')
    v("RED-042", "cookie-with-key", "Cookie: k=sk-abcdefghijklmnopqrst", "Cookie: [redacted]")
    v("RED-043", "set-cookie-jwt", "Set-Cookie: auth=eyJhbGciOi.eyJzdWIi.sig", "Set-Cookie: [redacted]")
    v("RED-044", "email-and-path",
      "alice@example.com wrote /home/alice/notes.md", "a***@example.com wrote ~/notes.md")
    v("RED-045", "header-then-email-then-key",
      "Authorization: Bearer abc\nuser: dave@example.com\nretry: sk-abcdefghijklmnopqrs",
      "Authorization: [redacted]\nuser: d***@example.com\nretry: sk-a\u2026pqrs")
    v("RED-046", "key-inside-json-string-value",
      '{"raw":"failed with sk-abcdefghijklmnopqrstuvwx"}',
      '{"raw":"failed with sk-a\u2026uvwx"}')
    return V


# ================================================================ 组装与产物
def all_cases():
    cases = []
    for fn in (frame_cases, oai_cases, resp_cases, anth_cases, gem_cases, ort_cases,
               err_cases, can_cases, render_cases, export_cases):
        cases.extend(fn())
    return cases


def tree_hash(root):
    entries = []
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in sorted(filenames):
            path = os.path.join(dirpath, name)
            rel = os.path.relpath(path, root).replace("\\", "/")
            with open(path, "rb") as f:
                data = f.read()
            entries.append((rel, len(data), hashlib.sha256(data).hexdigest()))
    entries.sort()
    h = hashlib.sha256()
    for rel, size, digest in entries:
        h.update(("%s\0%d\0%s\n" % (rel, size, digest)).encode("utf-8"))
    return h.hexdigest(), entries


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--hash", action="store_true", help="只打印目录树 sha256")
    ap.add_argument("--manifest", action="store_true", help="打印 ID + 文件数清单")
    ap.add_argument("--keep", action="store_true", help="不清空 samples/（默认先清空再生成）")
    args = ap.parse_args(argv)

    if not args.hash:
        if not args.keep and os.path.isdir(SAMPLES):
            for name in sorted(os.listdir(SAMPLES)):
                p = os.path.join(SAMPLES, name)
                if os.path.isdir(p):
                    for f in os.listdir(p):
                        os.remove(os.path.join(p, f))
                    os.rmdir(p)
                else:
                    os.remove(p)
        os.makedirs(SAMPLES, exist_ok=True)
        cases = all_cases()
        ids = [c.cid for c in cases]
        assert len(ids) == len(set(ids)), "用例 ID 重复"
        for c in cases:
            c.write()
        vectors = redaction_vectors()
        os.makedirs(os.path.dirname(VECTORS), exist_ok=True)
        with open(VECTORS, "w", encoding="utf-8", newline="\n") as f:
            for row in vectors:
                f.write(json.dumps({"id": row["id"], "rule": row["rule"],
                                    "input": row["input"], "expected": row["expected"]},
                                   ensure_ascii=False, sort_keys=True,
                                   separators=(",", ":")) + "\n")
        print("cases=%d vectors=%d" % (len(cases), len(vectors)))

    h, entries = tree_hash(SAMPLES)
    if args.manifest or not args.hash:
        print("\n%-11s %-6s %-9s %s" % ("CASE", "FILES", "BYTES", "FILES"))
        total_bytes = 0
        for d in sorted(os.listdir(SAMPLES)):
            p = os.path.join(SAMPLES, d)
            if not os.path.isdir(p):
                continue
            files = sorted(os.listdir(p))
            size = sum(os.path.getsize(os.path.join(p, f)) for f in files)
            total_bytes += size
            print("%-11s %-6d %-9d %s" % (d, len(files), size, ",".join(files)))
        print("\ndirs=%d files=%d bytes=%d" % (
            len([d for d in os.listdir(SAMPLES) if os.path.isdir(os.path.join(SAMPLES, d))]),
            len(entries), total_bytes))
    print("\nsamples_tree_sha256=%s" % h)
    return 0


if __name__ == "__main__":
    sys.exit(main())
