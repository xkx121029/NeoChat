"""errors.md §2/§3/§5 的错误对象构造表（conformance 共用，不是产品实现）。

为什么集中在这一张表：`failed.error` 会进入 events.jsonl 的字节比对，
所以 `title_key` / `causes` / `actions` 的取值必须双端一字不差。表里的
每一个 code、stage、retryable、causes、actions 都逐条抄自 errors.md §3，
slug 与 action 参数是对该表的中文描述的机械转写。任何一端自创文案键，
SHA-256 比对立刻红——这正是它要防的。
"""
import json

# code -> (stage, retryable, causes[], actions[])
# causes 是 errors.md §3 "候选原因" 列的 slug 转写，顺序即可能性降序
# actions 是 "可点操作" 列的转写；带参数的 action 把参数平铺进对象
TABLE = {
    "NET_CONNECTION_RESET": ("stream_read", True,
                             ["network_switch", "proxy_interrupted", "server_closed"],
                             [{"a": "retry"}, {"a": "check_proxy"}]),
    "NET_READ_TIMEOUT": ("stream_read", True,
                         ["model_queued", "proxy_buffered", "request_too_large"],
                         [{"a": "retry"}, {"a": "switch_model"}, {"a": "check_proxy"}]),
    "AUTH_MISSING_KEY": ("config", False,
                         ["first_use_unfilled", "key_deleted", "all_keys_disabled"],
                         [{"a": "edit_provider", "focus": "key"}]),
    "AUTH_INVALID_KEY": ("provider", False,
                         ["revoked", "wrong_endpoint", "truncated_paste"],
                         [{"a": "edit_provider", "focus": "key"}, {"a": "check_base_url"},
                          {"a": "copy_diagnostics"}]),
    "AUTH_INSUFFICIENT_QUOTA": ("provider", False,
                                ["balance_exhausted", "free_quota_expired", "org_quota_limited"],
                                [{"a": "top_up"}, {"a": "switch_model"}, {"a": "manage_keys"}]),
    "RATE_LIMITED": ("provider", True,
                     ["rpm_exceeded", "concurrency_exceeded", "daily_quota_exhausted"],
                     [{"a": "retry_after"}, {"a": "edit_provider"}, {"a": "manage_keys"}]),
    "MODEL_NOT_FOUND": ("provider", False,
                        ["model_retired", "model_id_typo", "endpoint_lacks_model"],
                        [{"a": "refresh_models"}, {"a": "edit_provider"}]),
    "PARAM_INVALID": ("provider", False,
                      ["unsupported_sampling", "value_out_of_range", "protocol_fields_merged"],
                      [{"a": "view_raw"}, {"a": "edit_provider"}, {"a": "switch_model"}]),
    "CONTEXT_OVERFLOW": ("provider", False,
                         ["history_too_long", "attachment_too_large", "window_misconfigured"],
                         [{"a": "reduce_context"}, {"a": "switch_model"}, {"a": "edit_provider"}]),
    "CAPABILITY_UNSUPPORTED": ("config", False,
                               ["no_vision", "no_tools", "no_structured_output"],
                               [{"a": "switch_model"}, {"a": "edit_provider"},
                                {"a": "view_capability_matrix"}]),
    "CONTENT_FILTERED": ("provider", False,
                         ["input_blocked", "output_blocked", "region_restricted"],
                         [{"a": "retry"}, {"a": "switch_model"}]),
    "PARSE_SSE_INVALID": ("parse", False,
                          ["non_stream_endpoint", "proxy_html_injected", "json_error_body"],
                          [{"a": "check_base_url"}, {"a": "check_proxy"}, {"a": "view_raw"}]),
    "PARSE_INCOMPLETE": ("parse", True,
                         ["network_interrupted", "server_closed_early", "proxy_truncated"],
                         [{"a": "retry"}, {"a": "check_proxy"}]),
    "PARSE_EVENT_TOO_LARGE": ("parse", False,
                              ["non_stream_large_response", "proxy_duplicated_data"],
                              [{"a": "check_base_url"}, {"a": "view_raw"}]),
    "SERVER_ERROR": ("provider", True,
                     ["provider_outage", "model_restarting", "region_degraded"],
                     [{"a": "retry"}, {"a": "switch_model"}, {"a": "view_service_status"}]),
    "SERVER_OVERLOADED": ("provider", True,
                          ["peak_queue", "account_downgraded", "region_capacity"],
                          [{"a": "retry_after"}, {"a": "switch_model"}]),
    "ENDPOINT_NOT_FOUND": ("provider", False,
                           ["base_url_v1", "protocol_mismatch", "custom_path"],
                           [{"a": "check_base_url"}, {"a": "edit_provider"},
                            {"a": "open_docs", "topic": "base_url"}]),
    "UNKNOWN_PROVIDER_ERROR": ("provider", False,
                               ["format_changed", "custom_relay", "proxy_rewritten"],
                               [{"a": "view_raw"}, {"a": "copy_diagnostics"},
                                {"a": "switch_model"}]),
    "CONFIG_INVALID_BASE_URL": ("config", False,
                                ["missing_scheme", "space_or_cjk_punct", "web_page_url"],
                                [{"a": "check_base_url"}, {"a": "open_docs", "topic": "base_url"}]),
    "KEY_STORE_UNAVAILABLE": ("crypto", False,
                              ["lock_credential_changed", "keystore_reset", "strongbox_fallback_failed"],
                              [{"a": "edit_provider", "focus": "key"}, {"a": "copy_diagnostics"}]),
    "DB_WRITE_FAILED": ("persist", False,
                        ["disk_full", "db_locked_or_corrupt", "storage_permission_revoked"],
                        [{"a": "clean_storage"}, {"a": "db_check"}]),
    "TOOL_EXEC_FAILED": ("tool", False,
                         ["tool_service_unreachable", "args_schema_mismatch", "mcp_process_exited"],
                         [{"a": "retry_tool"}, {"a": "open_docs", "topic": "tools"},
                          {"a": "open_mcp_settings"}]),
    "INTERNAL": ("local", False,
                 ["internal_defect", "inconsistent_state"],
                 [{"a": "copy_diagnostics"}, {"a": "retry"}]),
    "IMPORT_MALFORMED": ("persist", False,
                         ["not_neochat_export", "truncated_json", "depth_or_field_limit"],
                         [{"a": "view_raw"}, {"a": "open_docs", "topic": "import"}]),
    "IMPORT_TOO_LARGE": ("persist", False,
                         ["too_many_attachments", "too_many_conversations", "double_export"],
                         [{"a": "open_docs", "topic": "import"}, {"a": "clean_storage"}]),
    "IMPORT_UNSUPPORTED_VERSION": ("persist", False,
                                   ["newer_version", "format_version_not_1"],
                                   [{"a": "open_docs", "topic": "import"},
                                    {"a": "copy_diagnostics"}]),
}

RAW_LIMIT = 512
RETRY_AFTER_CAP_MS = 30000


def make(code, meta, http_status=None, provider_code=None, params=None, raw=None,
         request_id=None, retry_after_ms=None, trace_id=None):
    """构造 errors.md §2 的错误对象。字段顺序无关（输出前键排序）。"""
    if code not in TABLE:
        raise KeyError("unknown error code: %s" % code)
    stage, retryable, causes, actions = TABLE[code]
    err = {
        "code": code,
        "stage": stage,
        "retryable": retryable,
        "title_key": "errors.%s.title" % code,
        "params": dict(params or {}),
        "causes": ["errors.%s.cause.%s" % (code, c) for c in causes],
        "actions": [dict(a) for a in actions],
    }
    prov = meta.get("provider") or {}
    if prov.get("name"):
        err["provider"] = prov["name"]
    if prov.get("model"):
        err["model"] = prov["model"]
    if http_status is not None:
        err["http_status"] = http_status
    if provider_code:
        err["provider_code"] = provider_code
    if retry_after_ms is not None:
        err["retry_after_ms"] = min(int(retry_after_ms), RETRY_AFTER_CAP_MS)
    if raw is not None:
        err["raw"] = raw
    if request_id:
        err["request_id"] = request_id
    # 非确定性字段：runner 比对前剔除（CASES.md §0）
    err["trace_id"] = trace_id or "00000000-0000-7000-8000-000000000000"
    return err


def _body_text(body):
    if isinstance(body, bytes):
        return body.decode("utf-8", "replace")
    return body or ""


def _json(body):
    try:
        return json.loads(_body_text(body))
    except Exception:
        return None


def from_http(meta, status, body, headers, provider_code_hint=None, retry_after_ms=None):
    """errors.md §5：按 HTTP 状态 + 响应体映射，闭集内优先。"""
    from redactor import redact

    protocol = meta.get("protocol")
    raw = redact(_body_text(body))
    obj = _json(body)
    code = None
    pcode = provider_code_hint

    msg = ""
    err_obj = {}
    if isinstance(obj, dict):
        err_obj = obj.get("error") if isinstance(obj.get("error"), dict) else obj
        if isinstance(err_obj, dict):
            msg = str(err_obj.get("message") or err_obj.get("error") or "")
            pcode = pcode or err_obj.get("code") or err_obj.get("type") or err_obj.get("status")
    blob = (msg + " " + _body_text(body)).lower()

    if protocol == "anthropic":
        et = (err_obj.get("type") if isinstance(err_obj, dict) else None) or ""
        if et == "authentication_error" or et == "permission_error":
            code = "AUTH_INVALID_KEY"
        elif et == "rate_limit_error":
            code = "RATE_LIMITED"
        elif et in ("insufficient_quota",) or status == 402:
            code = "AUTH_INSUFFICIENT_QUOTA"
        elif et == "invalid_request_error" and "prompt is too long" in blob:
            code = "CONTEXT_OVERFLOW"
        elif et == "invalid_request_error":
            code = "PARAM_INVALID"
        elif et == "overloaded_error":
            code = "SERVER_OVERLOADED"
        elif et == "api_error":
            code = "SERVER_ERROR"
        elif et == "request_too_large":
            code = "CONTEXT_OVERFLOW"
        elif et == "not_found_error":
            code = "MODEL_NOT_FOUND"
        else:
            code = None
    elif protocol == "gemini":
        if status == 400 and ("api_key_invalid" in blob or "api key not valid" in blob):
            code = "AUTH_INVALID_KEY"
        elif status == 400 and ("token count" in blob or "too long" in blob):
            code = "CONTEXT_OVERFLOW"
        elif status == 400:
            code = "PARAM_INVALID"
        elif status == 403:
            code = "AUTH_INVALID_KEY"
        elif status == 404:
            code = "MODEL_NOT_FOUND"
        elif status == 429:
            code = "RATE_LIMITED"
        elif status in (500, 503):
            code = "SERVER_ERROR"
        elif status == 402 or "billing" in blob:
            code = "AUTH_INSUFFICIENT_QUOTA"
        else:
            code = None
    else:  # openai / openai_compatible / openrouter / responses
        if status == 200:
            # HTTP 200 但响应体是 JSON 错误对象（stream-events §2.3）
            if (str(pcode or "").lower() == "insufficient_quota"
                    or "insufficient_quota" in blob):
                code = "AUTH_INSUFFICIENT_QUOTA"
            elif "context_length_exceeded" in blob:
                code = "CONTEXT_OVERFLOW"
            elif "content_filter" in blob or "content_policy" in blob:
                code = "CONTENT_FILTERED"
            elif status == 200:
                code = "PARSE_SSE_INVALID"
        elif status == 401:
            code = "AUTH_INVALID_KEY"
        elif status == 403:
            code = "CONTENT_FILTERED" if ("country" in blob or "region" in blob) else "AUTH_INVALID_KEY"
        elif status == 404:
            code = "MODEL_NOT_FOUND" if "model" in blob else "ENDPOINT_NOT_FOUND"
        elif status == 429:
            if (str(pcode or "").lower() == "insufficient_quota"
                    or "insufficient_quota" in blob):
                code = "AUTH_INSUFFICIENT_QUOTA"
            else:
                code = "RATE_LIMITED"
        elif status == 400:
            if "context_length_exceeded" in blob:
                code = "CONTEXT_OVERFLOW"
            elif "content_filter" in blob or "content_policy" in blob:
                code = "CONTENT_FILTERED"
            else:
                code = "PARAM_INVALID"
        elif status == 413:
            code = "CONTEXT_OVERFLOW"
        elif status in (500, 502, 503, 504):
            code = "SERVER_ERROR"
        elif status == 529:
            code = "SERVER_OVERLOADED"
        elif status == 402:
            code = "AUTH_INSUFFICIENT_QUOTA"

    if code is None:
        code = "UNKNOWN_PROVIDER_ERROR"

    retry_after = retry_after_ms
    if retry_after is None and code == "RATE_LIMITED":
        ra = (headers or {}).get("retry-after")
        if ra:
            try:
                retry_after = int(float(ra) * 1000)
            except ValueError:
                retry_after = None
    if code == "RATE_LIMITED" and retry_after is not None:
        params = {"retry_after_ms": str(min(retry_after, RETRY_AFTER_CAP_MS))}
    else:
        params = {}
    return make(code, meta, http_status=status, provider_code=str(pcode) if pcode else None,
                params=params, raw=raw, retry_after_ms=retry_after)


def from_sse_error(meta, err_obj, http_status=None):
    """流内错误事件（Anthropic `error`、OpenAI/ORT `data:{"error":...}`）。"""
    from redactor import redact

    if not isinstance(err_obj, dict):
        return make("PARSE_SSE_INVALID", meta, http_status=http_status)
    inner = err_obj.get("error") if isinstance(err_obj.get("error"), dict) else err_obj
    ecode = inner.get("code")
    etype = str(inner.get("type") or (ecode if isinstance(ecode, str) else "") or "")
    blob = json.dumps(inner, ensure_ascii=False).lower()
    code = None
    code = {
        "authentication_error": "AUTH_INVALID_KEY",
        "permission_error": "AUTH_INVALID_KEY",
        "rate_limit_error": "RATE_LIMITED",
        "insufficient_quota": "AUTH_INSUFFICIENT_QUOTA",
        "overloaded_error": "SERVER_OVERLOADED",
        "api_error": "SERVER_ERROR",
        "server_error": "SERVER_ERROR",
        "request_too_large": "CONTEXT_OVERFLOW",
        "not_found_error": "MODEL_NOT_FOUND",
    }.get(etype)
    if code is None and etype == "invalid_request_error":
        code = "CONTEXT_OVERFLOW" if "prompt is too long" in blob else "PARAM_INVALID"
    if code is None:
        if ecode == "insufficient_quota" or "insufficient_quota" in blob:
            code = "AUTH_INSUFFICIENT_QUOTA"
        elif "context_length_exceeded" in blob:
            code = "CONTEXT_OVERFLOW"
        elif "content_filter" in blob or "content_policy" in blob:
            code = "CONTENT_FILTERED"
        elif isinstance(ecode, int) and 500 <= ecode < 600:
            code = "SERVER_OVERLOADED" if ecode == 529 else "SERVER_ERROR"
        elif str(ecode) in ("500", "502", "503", "504"):
            code = "SERVER_ERROR"
        elif str(ecode) == "529":
            code = "SERVER_OVERLOADED"
    if code is None:
        code = "UNKNOWN_PROVIDER_ERROR"
    return make(code, meta, http_status=http_status,
                provider_code=str(ecode or etype) if (ecode or etype) else None,
                raw=redact(json.dumps(err_obj, ensure_ascii=False)))
