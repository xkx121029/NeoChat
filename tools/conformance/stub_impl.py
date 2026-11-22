"""桩实现：正确解析流式字节的最小 Python 协议层。

它只服务于 conformance 端到端验证（CASES.md §0 的"被测实现"位），不是产品代码，
也不追求性能。它实现的是 spec/stream-events.md 的 L1 分帧与 L2 归一化：
增量 UTF-8 解码、三種行尾、BOM、非法字节替换、心跳丢弃、16 MiB 帧上限、
gzip 透明解压、四家 provider 映射、errors.md §5 错误映射、取消语义。

接口（runner.py 调用）：
    adapter = Adapter(meta, reader)
    for ev in adapter.events(): ...      # 事件按 seq 递增产出
    adapter.cancel()                     # 测试钩子：模拟用户取消
    adapter.socket_closed                # 终止后是否已关闭连接

`reader.read(timeout_ms)` 返回 bytes（b"" 表示 EOF），或抛 ReadTimeout / ConnectionReset。
"""
import codecs
import json
import zlib

import errors_model
from redactor import redact

MAX_FRAME = 16 * 1024 * 1024


class ReadTimeout(Exception):
    pass


class ConnectionReset(Exception):
    pass


class FrameTooLarge(Exception):
    pass


class ParseInvalid(Exception):
    pass


class GzipReader(object):
    def __init__(self, inner):
        self.inner = inner
        self.d = zlib.decompressobj(16 + zlib.MAX_WBITS)
        self.buf = b""
        self.eof = False

    def read(self, timeout_ms):
        while not self.buf:
            if self.eof:
                return b""
            try:
                chunk = self.inner.read(timeout_ms)
            except ConnectionReset:
                raise  # 压缩流截断 → 读错误 → NET_CONNECTION_RESET（stream-events §2.4）
            if chunk == b"":
                self.eof = True
                self.buf = self.d.flush()
                break
            try:
                self.buf = self.d.decompress(chunk)
            except zlib.error:
                raise ConnectionReset()
        out, self.buf = self.buf, b""
        return out

    def close(self):
        self.inner.close()

    @property
    def closed(self):
        return self.inner.closed


class _NullReader(object):
    def __init__(self):
        self.closed = False

    def read(self, timeout_ms):
        return b""

    def close(self):
        self.closed = True


class Adapter(object):
    def __init__(self, meta, reader=None):
        self.meta = meta
        self.protocol = meta.get("protocol") or "openai"
        self.reader = reader or _NullReader()
        if (meta.get("content_encoding") or "").lower() == "gzip":
            self.reader = GzipReader(self.reader)
        self.seq = 0
        self.cancelled = False
        self.socket_closed = False
        self.text_len = 0
        self.preamble = b""
        self.bom_pending = True
        self.decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.invalid_utf8_bytes = 0
        self.read_idle_ms = int((meta.get("test_config") or {}).get("read_idle_ms", 180000))
        self.terminal = False
        self.open_tool_indices = set()
        self.closed_tool_indices = set()
        self.tool_seq = 0                 # anthropic/gemini/responses 的 eventIndex 计数器
        self.block_index_map = {}         # anthropic provider block index -> eventIndex
        self.resp_item_map = {}           # responses item_id -> eventIndex
        self.finish_reason = None
        self.usage_obj = None
        self.usage_input = 0
        self.usage_output = 0
        self.usage_cached = 0
        self.usage_reasoning = 0
        self.usage_total = None
        self.emitted_prefix = ""
        self.cumulative = bool(meta.get("cumulative_stream"))
        self.duplicate = bool(meta.get("duplicate"))
        self.noticed_multi_choice = False

    # ---------------- 事件构造 ----------------
    def _mk(self, t, **fields):
        ev = {"t": t, "seq": self.seq}
        self.seq += 1
        ev.update(fields)
        return ev

    def _failed(self, code, **kw):
        return self._mk("failed", error=errors_model.make(code, self.meta, **kw))

    def _close(self):
        try:
            self.reader.close()
        finally:
            self.socket_closed = True

    def cancel(self):
        self.cancelled = True

    # ---------------- 传输层 ----------------
    def _read_transport(self):
        return self.reader.read(self.read_idle_ms)

    def _byte_chunks(self):
        """原始字节片：先吐 peek 回填的前导，再吐传输层；首部 BOM 丢弃。"""
        while True:
            if self.preamble:
                chunk, self.preamble = self.preamble, b""
            else:
                chunk = self._read_transport()
                if chunk == b"":
                    return
            if self.bom_pending:
                chunk, self.preamble = chunk + self.preamble, b""
                if len(chunk) < 3:
                    self.preamble = chunk
                    continue
                self.bom_pending = False
                if chunk.startswith(b"\xef\xbb\xbf"):
                    chunk = chunk[3:]
                if not chunk:
                    continue
            yield chunk

    def _text_chunks(self):
        for chunk in self._byte_chunks():
            text = self.decoder.decode(chunk, False)
            if "\ufffd" in text:
                self.invalid_utf8_bytes += text.count("\ufffd")
            if text:
                yield text
        tail = self.decoder.decode(b"", True)
        if tail:
            yield tail

    def _lines(self):
        buf = ""
        for piece in self._text_chunks():
            buf += piece
            while True:
                nl = buf.find("\n")
                cr = buf.find("\r")
                cand = [x for x in (nl, cr) if x >= 0]
                if not cand:
                    break
                idx = min(cand)
                if buf[idx] == "\r":
                    if idx == len(buf) - 1:
                        break  # 可能是 \r\n 的前半，等下一片
                    if buf[idx + 1] == "\n":
                        yield buf[:idx]
                        buf = buf[idx + 2:]
                    else:
                        yield buf[:idx]
                        buf = buf[idx + 1:]
                else:
                    yield buf[:idx]
                    buf = buf[idx + 1:]
        if buf:
            if buf.endswith("\r"):
                yield buf[:-1]
                yield ""
            else:
                yield buf

    def _frames(self):
        event_name = None
        data_lines = []
        payload_len = 0
        for line in self._lines():
            if line == "":
                if data_lines:
                    yield (event_name, "\n".join(data_lines))
                event_name = None
                data_lines = []
                payload_len = 0
                continue
            if line.startswith(":"):
                continue  # 心跳：丢弃，只重置 idle
            if ":" in line:
                field, rest = line.split(":", 1)
                value = rest[1:] if rest.startswith(" ") else rest
            else:
                field, value = line, ""
            if field == "data":
                payload_len += len(value.encode("utf-8"))
                if payload_len > MAX_FRAME:
                    raise FrameTooLarge()
                data_lines.append(value)
            elif field == "event":
                event_name = value
            # id / retry：保留在帧对象里，本桩不消费
        if data_lines:
            yield (event_name, "\n".join(data_lines))

    # ---------------- 入口 ----------------
    def events(self):
        try:
            for ev in self._drive():
                yield ev
        except ReadTimeout:
            yield self._failed("NET_READ_TIMEOUT")
            self._close()
        except ConnectionReset:
            yield self._failed("NET_CONNECTION_RESET")
            self._close()
        except FrameTooLarge:
            yield self._failed("PARSE_EVENT_TOO_LARGE")
            self._close()
        except ParseInvalid:
            yield self._failed("PARSE_SSE_INVALID")
            self._close()

    def _peek(self, want=512):
        buf = self.preamble
        self.preamble = b""
        while len(buf) < want:
            chunk = self._read_transport()
            if chunk == b"":
                break
            buf += chunk
        self.preamble = buf
        return buf

    def _drive(self):
        status = int(self.meta.get("http_status", 200))
        headers = {k.lower(): v for k, v in (self.meta.get("http_headers") or {}).items()}
        ctype = str(headers.get("content-type") or "").lower()

        if status < 200 or status >= 300:
            body = self._peek(65536)
            yield self._mk("failed", error=errors_model.from_http(self.meta, status, body, headers))
            self._close()
            return

        if self.cancelled:  # CAN-001：连接建立、首字节之前取消
            yield self._mk("done", finish_reason="aborted_user")
            self._close()
            return

        head = self._peek(64)
        head_text = head.lstrip(b"\xef\xbb\xbf").lstrip()
        is_array = head_text.startswith(b"[")
        looks_json_obj = head_text.startswith(b"{")

        if self.protocol == "gemini" and is_array:
            yield from self._drive_gemini_array()
            return
        if looks_json_obj and (ctype.startswith("application/json") or "text/event-stream" not in ctype):
            body = self._peek(65536)
            yield self._mk("failed", error=errors_model.from_http(
                self.meta, status, body, headers))
            self._close()
            return

        handler = {
            "openai": self._frame_openai,
            "openai_compatible": self._frame_openai,
            "openrouter": self._frame_openai,
            "responses": self._frame_responses,
            "anthropic": self._frame_anthropic,
            "gemini": self._frame_gemini,
        }.get(self.protocol)
        if handler is None:
            raise ParseInvalid()

        for event_name, data in self._frames():
            if self.cancelled:
                break
            for out in handler(event_name, data):
                yield out
            if self.terminal:
                return

        if self.cancelled:
            yield self._mk("done", finish_reason="aborted_user")
            self._close()
            return
        for out in self._on_eof():
            yield out

    def _on_eof(self):
        if self.protocol in ("openai", "openai_compatible", "openrouter"):
            if self.finish_reason is not None:
                for out in self._finish_openai():
                    yield out
            else:
                yield self._incomplete()
        elif self.protocol == "responses":
            if self.finish_reason is not None:
                for out in self._finish_responses():
                    yield out
            else:
                yield self._incomplete()
        elif self.protocol == "gemini":
            if self.finish_reason is not None:
                for out in self._finish_gemini():
                    yield out
            else:
                yield self._incomplete()
        elif self.protocol == "anthropic":
            if self.terminal:
                return
            if self.stop_reason is not None:
                for out in self._finish_anthropic():
                    yield out
            else:
                yield self._incomplete()

    def _incomplete(self):
        self.terminal = True
        self._close()
        return self._failed("PARSE_INCOMPLETE",
                            params={"partial_text_length": str(self.text_len)})

    # ---------------- usage ----------------
    def _usage(self):
        if self.usage_obj is not None:
            return dict(self.usage_obj)
        total = self.usage_total
        if total is None:
            total = self.usage_input + self.usage_output
        u = {"input": self.usage_input, "output": self.usage_output,
             "cached": self.usage_cached, "total": total, "estimated": False}
        if self.usage_reasoning:
            u["reasoning"] = self.usage_reasoning
        return u

    # ---------------- OpenAI Chat Completions ----------------
    def _dedupe(self, text):
        if not (self.cumulative or self.duplicate):
            return text
        if text.startswith(self.emitted_prefix):
            new = text[len(self.emitted_prefix):]
            self.emitted_prefix = text
            return new
        return ""  # 重写而非累积：丢弃（trace: cumulative_rewrite_ignored）

    def _frame_openai(self, event_name, data):
        s = data.strip()
        if not s:
            return
        if s == "[DONE]":
            for out in self._finish_openai():
                yield out
            return
        try:
            obj = json.loads(s)
        except ValueError:
            raise ParseInvalid()
        if isinstance(obj, dict) and obj.get("error"):
            self.terminal = True
            self._close()
            yield self._mk("failed", error=errors_model.from_sse_error(self.meta, obj))
            return
        if isinstance(obj, dict) and obj.get("usage"):
            u = obj["usage"]
            inp = u.get("prompt_tokens") or 0
            out = u.get("completion_tokens") or 0
            cached = (u.get("prompt_tokens_details") or {}).get("cached_tokens") or 0
            usage = {"input": inp, "output": out, "cached": cached,
                     "total": u.get("total_tokens") or inp + out, "estimated": False}
            if u.get("cost") is not None:
                amount = format(float(u["cost"]), ".8f").rstrip("0").rstrip(".")
                usage["provider_cost"] = {"amount": amount, "currency": "USD"}
            self.usage_obj = usage
        choices = obj.get("choices") or []
        if len(choices) > 1 and not self.noticed_multi_choice:
            self.noticed_multi_choice = True
            yield self._mk("notice", code="multi_choice_ignored", params={})
        if not choices:
            return
        choice = choices[0] or {}
        delta = choice.get("delta") or {}
        text = delta.get("content")
        if isinstance(text, str) and text != "":
            piece = self._dedupe(text)
            if piece:
                self.text_len += len(piece)
                yield self._mk("delta", text=piece)
        reason = delta.get("reasoning_content")
        if reason is None:
            reason = delta.get("reasoning")
        if isinstance(reason, str) and reason != "":
            yield self._mk("reasoning_delta", text=reason)
        fragments = sorted(delta.get("tool_calls") or [], key=lambda x: x.get("index") or 0)
        for tc in fragments:
            idx = int(tc.get("index") or 0)
            fn = tc.get("function") or {}
            if idx not in self.open_tool_indices:
                self.open_tool_indices.add(idx)
                yield self._mk("tool_call_delta", index=idx,
                               id=tc.get("id") or "call_%d" % idx,
                               name=fn.get("name") or "", args=fn.get("arguments") or "")
            else:
                args = fn.get("arguments")
                if args:
                    yield self._mk("tool_call_delta", index=idx, args=args)
        if choice.get("finish_reason"):
            self.finish_reason = choice["finish_reason"]

    def _finish_openai(self):
        self.terminal = True
        for idx in sorted(self.open_tool_indices - self.closed_tool_indices):
            self.closed_tool_indices.add(idx)
            yield self._mk("tool_call_end", index=idx)
        if self.usage_obj is not None:
            yield self._mk("usage", **self.usage_obj)
        finish = {"stop": "stop", "length": "length", "tool_calls": "tool_calls",
                  "function_call": "tool_calls", "content_filter": "content_filter"}
        fr = finish.get(self.finish_reason)
        if fr is None:
            fr = "tool_calls" if self.open_tool_indices else "stop"
        yield self._mk("done", finish_reason=fr)
        self._close()

    # ---------------- OpenAI Responses ----------------
    def _frame_responses(self, event_name, data):
        s = data.strip()
        if not s:
            return
        try:
            obj = json.loads(s)
        except ValueError:
            raise ParseInvalid()
        etype = obj.get("type") or event_name or ""
        if etype in ("response.created", "response.in_progress"):
            return
        if etype == "response.output_text.delta":
            text = obj.get("delta")
            if isinstance(text, str) and text:
                self.text_len += len(text)
                yield self._mk("delta", text=text)
        elif etype in ("response.reasoning_summary_text.delta", "response.reasoning_text.delta"):
            text = obj.get("delta")
            if isinstance(text, str) and text:
                yield self._mk("reasoning_delta", text=text)
        elif etype == "response.output_item.added":
            item = obj.get("item") or {}
            if item.get("type") == "function_call":
                idx = self.tool_seq
                self.tool_seq += 1
                self.resp_item_map[item.get("id")] = idx
                self.open_tool_indices.add(idx)
                yield self._mk("tool_call_delta", index=idx,
                               id=item.get("call_id") or item.get("id") or "call_%d" % idx,
                               name=item.get("name") or "", args="")
        elif etype == "response.function_call_arguments.delta":
            idx = self.resp_item_map.get(obj.get("item_id"))
            delta = obj.get("delta")
            if idx is not None and isinstance(delta, str) and delta:
                yield self._mk("tool_call_delta", index=idx, args=delta)
        elif etype == "response.function_call_arguments.done":
            idx = self.resp_item_map.get(obj.get("item_id"))
            if idx is not None and idx not in self.closed_tool_indices:
                self.closed_tool_indices.add(idx)
                yield self._mk("tool_call_end", index=idx)
        elif etype == "response.completed":
            resp = obj.get("response") or {}
            u = resp.get("usage")
            if u:
                inp = u.get("input_tokens") or 0
                out = u.get("output_tokens") or 0
                cached = (u.get("input_tokens_details") or {}).get("cached_tokens") or 0
                reasoning = (u.get("output_tokens_details") or {}).get("reasoning_tokens") or 0
                self.usage_obj = {"input": inp, "output": out, "cached": cached,
                                  "total": u.get("total_tokens") or inp + out,
                                  "estimated": False}
                if reasoning:
                    self.usage_obj["reasoning"] = reasoning
            incompl = resp.get("incomplete_details") or {}
            if incompl.get("reason") == "max_output_tokens":
                self.finish_reason = "length"
            elif self.open_tool_indices:
                self.finish_reason = "tool_calls"
            else:
                self.finish_reason = "stop"
            for out in self._finish_responses():
                yield out
        elif etype in ("response.failed", "error"):
            self.terminal = True
            self._close()
            if etype == "response.failed":
                inner = (obj.get("response") or {}).get("error") or {}
            else:
                inner = obj.get("error") or obj
            yield self._mk("failed", error=errors_model.from_sse_error(
                self.meta, {"error": inner}))

    def _finish_responses(self):
        self.terminal = True
        for idx in sorted(self.open_tool_indices - self.closed_tool_indices):
            self.closed_tool_indices.add(idx)
            yield self._mk("tool_call_end", index=idx)
        if self.usage_obj is not None:
            yield self._mk("usage", **self.usage_obj)
        yield self._mk("done", finish_reason=self.finish_reason or "stop")
        self._close()

    # ---------------- Anthropic ----------------
    def _frame_anthropic(self, event_name, data):
        s = data.strip()
        if not s:
            return
        try:
            obj = json.loads(s)
        except ValueError:
            raise ParseInvalid()
        etype = obj.get("type") or event_name or ""
        if etype == "ping":
            return
        if etype == "message_start":
            m = obj.get("message") or {}
            u = m.get("usage") or {}
            self.usage_input = u.get("input_tokens") or 0
            self.usage_cached = u.get("cache_read_input_tokens") or 0
        elif etype == "content_block_start":
            block = obj.get("content_block") or {}
            bidx = obj.get("index")
            if block.get("type") == "tool_use":
                idx = self.tool_seq
                self.tool_seq += 1
                self.block_index_map[bidx] = idx
                self.open_tool_indices.add(idx)
                yield self._mk("tool_call_delta", index=idx,
                               id=block.get("id") or "call_%d" % idx,
                               name=block.get("name") or "", args="")
        elif etype == "content_block_delta":
            delta = obj.get("delta") or {}
            dtype = delta.get("type")
            if dtype == "text_delta":
                text = delta.get("text")
                if text:
                    self.text_len += len(text)
                    yield self._mk("delta", text=text)
            elif dtype == "thinking_delta":
                text = delta.get("thinking")
                if text:
                    yield self._mk("reasoning_delta", text=text)
            elif dtype == "input_json_delta":
                idx = self.block_index_map.get(obj.get("index"))
                piece = delta.get("partial_json")
                if idx is not None and piece:
                    yield self._mk("tool_call_delta", index=idx, args=piece)
        elif etype == "content_block_stop":
            idx = self.block_index_map.get(obj.get("index"))
            if idx is not None and idx not in self.closed_tool_indices:
                self.closed_tool_indices.add(idx)
                yield self._mk("tool_call_end", index=idx)
        elif etype == "message_delta":
            delta = obj.get("delta") or {}
            if delta.get("stop_reason"):
                self.stop_reason = delta["stop_reason"]
            u = obj.get("usage") or {}
            if u.get("output_tokens") is not None:
                self.usage_output = u["output_tokens"]
        elif etype == "message_stop":
            for out in self._finish_anthropic():
                yield out
        elif etype == "error":
            self.terminal = True
            self._close()
            yield self._mk("failed", error=errors_model.from_sse_error(self.meta, obj))

    def _finish_anthropic(self):
        self.terminal = True
        for idx in sorted(self.open_tool_indices - self.closed_tool_indices):
            self.closed_tool_indices.add(idx)
            yield self._mk("tool_call_end", index=idx)
        yield self._mk("usage", **self._usage())
        fr = {"end_turn": "stop", "stop_sequence": "stop", "max_tokens": "length",
              "tool_use": "tool_calls", "refusal": "content_filter"}.get(self.stop_reason)
        if fr is None:
            fr = "tool_calls" if self.open_tool_indices else "stop"
        yield self._mk("done", finish_reason=fr)
        self._close()

    # ---------------- Gemini ----------------
    def _gemini_object(self, obj):
        fb = obj.get("promptFeedback") or {}
        cands = obj.get("candidates") or []
        if fb.get("blockReason") and not cands:
            self.terminal = True
            self._close()
            yield self._failed("CONTENT_FILTERED")
            return
        if not cands:
            return
        cand = cands[0] or {}
        parts = ((cand.get("content") or {}).get("parts") or [])
        for part in parts:
            text = part.get("text")
            if isinstance(text, str) and text:
                piece = self._dedupe(text)
                if piece:
                    if part.get("thought"):
                        yield self._mk("reasoning_delta", text=piece)
                    else:
                        self.text_len += len(piece)
                        yield self._mk("delta", text=piece)
            fc = part.get("functionCall")
            if fc:
                idx = self.tool_seq
                self.tool_seq += 1
                args = json.dumps(fc.get("args") or {}, ensure_ascii=False,
                                  sort_keys=True, separators=(",", ":"))
                yield self._mk("tool_call_delta", index=idx, id="call_%d" % idx,
                               name=fc.get("name") or "", args=args)
                yield self._mk("tool_call_end", index=idx)
                self.open_tool_indices.discard(idx)
                self.closed_tool_indices.add(idx)
        if cand.get("finishReason"):
            self.finish_reason = cand["finishReason"]
        um = obj.get("usageMetadata")
        if um:
            inp = um.get("promptTokenCount") or 0
            out = um.get("candidatesTokenCount") or 0
            cached = um.get("cachedContentTokenCount") or 0
            reasoning = um.get("thoughtsTokenCount") or 0
            self.usage_obj = {"input": inp, "output": out, "cached": cached,
                              "total": um.get("totalTokenCount") or inp + out,
                              "estimated": False}
            if reasoning:
                self.usage_obj["reasoning"] = reasoning

    def _frame_gemini(self, event_name, data):
        s = data.strip()
        if not s:
            return
        try:
            obj = json.loads(s)
        except ValueError:
            raise ParseInvalid()
        for out in self._gemini_object(obj):
            yield out

    def _finish_gemini(self):
        self.terminal = True
        if self.usage_obj is not None:
            yield self._mk("usage", **self.usage_obj)
        fr = {"STOP": "stop", "MAX_TOKENS": "length", "SAFETY": "content_filter",
              "RECITATION": "content_filter", "BLOCKLIST": "content_filter",
              "PROHIBITED_CONTENT": "content_filter", "OTHER": "stop"}.get(self.finish_reason)
        if fr is None:
            fr = "tool_calls" if self.closed_tool_indices else "stop"
        yield self._mk("done", finish_reason=fr)
        self._close()

    def _drive_gemini_array(self):
        """数组模式：按顶层 JSON 值边界增量切对象，不累积全量（stream-events §4.4）。"""
        dec = self.decoder
        depth = 0
        in_str = False
        esc = False
        buf = []
        started = False
        for raw in self._byte_chunks():
            if self.cancelled:
                break
            for ch in dec.decode(raw, False):
                if not started:
                    if ch.isspace():
                        continue
                    started = True
                    if ch == "[":
                        continue
                if in_str:
                    if depth > 0:
                        buf.append(ch)
                    if esc:
                        esc = False
                    elif ch == "\\":
                        esc = True
                    elif ch == '"':
                        in_str = False
                    continue
                if ch == '"':
                    in_str = True
                    if depth > 0:
                        buf.append(ch)
                elif ch in "{[":
                    depth += 1
                    if depth > 0:
                        buf.append(ch)
                elif ch in "}]":
                    depth -= 1
                    if depth > 0:
                        buf.append(ch)
                    elif depth == 0 and ch == "}":
                        buf.append(ch)
                        payload = "".join(buf)
                        buf = []
                        if payload:
                            for out in self._gemini_object(json.loads(payload)):
                                yield out
                    else:
                        buf = []
                elif depth > 0:
                    buf.append(ch)
        for out in self._on_eof():
            yield out
