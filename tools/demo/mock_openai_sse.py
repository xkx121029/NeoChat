#!/usr/bin/env python3
"""本地 OpenAI 兼容 SSE mock，用于演示与回归：不联网、不需要真实 Key。

用法: python3 tools/demo/mock_openai_sse.py [port]
端点:
  GET  /v1/models
  POST /v1/chat/completions   (stream=true 时逐 token 下发 SSE)
"""
import json, sys, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8787

REPLY = [
    "你好，", "我是 NeoChat。", "\n\n这篇是**双端原生**的 BYOK 客户端：",
    "\n- 流式解析在协议层，双端逐字节一致\n",
    "- 渲染层用原生控件，不走 WebView\n",
    "- 密钥只存在本机 Keystore / DPAPI\n\n",
    "```kotlin\nval event: StreamEvent = parse(bytes)\n```\n",
    "现在这条回复就是逐 token 下发的，你看它是一点点长出来的。",
]

class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _json(self, code, obj):
        body = json.dumps(obj, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.rstrip('/').endswith('/models'):
            self._json(200, {"object": "list", "data": [
                {"id": "mock-chat", "object": "model", "owned_by": "neochat"}]})
        else:
            self._json(404, {"error": {"message": "not found", "type": "invalid_request_error"}})

    def do_POST(self):
        n = int(self.headers.get('Content-Length', 0))
        raw = self.rfile.read(n)
        try:
            req = json.loads(raw or b'{}')
        except Exception:
            req = {}
        model = req.get("model", "mock-chat")
        self.send_response(200)
        self.send_header('Content-Type', 'text/event-stream; charset=utf-8')
        self.send_header('Cache-Control', 'no-cache')
        self.send_header('Connection', 'keep-alive')
        self.end_headers()

        def emit(obj):
            self.wfile.write(("data: " + json.dumps(obj, ensure_ascii=False) + "\n\n").encode())
            self.wfile.flush()

        for i, tok in enumerate(REPLY):
            emit({"id": "chatcmpl-mock", "object": "chat.completion.chunk", "model": model,
                  "choices": [{"index": 0, "delta": {"content": tok}, "finish_reason": None}]})
            time.sleep(0.05)
        emit({"id": "chatcmpl-mock", "object": "chat.completion.chunk", "model": model,
              "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}],
              "usage": {"prompt_tokens": 42, "completion_tokens": len(REPLY), "total_tokens": 42 + len(REPLY)}})
        self.wfile.write(b"data: [DONE]\n\n")
        self.wfile.flush()

if __name__ == '__main__':
    print("mock OpenAI SSE on http://127.0.0.1:%d/v1" % PORT, flush=True)
    ThreadingHTTPServer(('127.0.0.1', PORT), H).serve_forever()
