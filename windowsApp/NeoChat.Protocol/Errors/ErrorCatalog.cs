using NeoChat.Protocol.Events;

namespace NeoChat.Protocol.Errors;

/// <summary>Normative error catalogue: spec/errors.md §3 (closed set of codes, causes and actions).</summary>
public static class ErrorCatalog
{
    public const string AuthInvalidKey = "AUTH_INVALID_KEY";
    public const string AuthInsufficientQuota = "AUTH_INSUFFICIENT_QUOTA";
    public const string RateLimited = "RATE_LIMITED";
    public const string ContextOverflow = "CONTEXT_OVERFLOW";
    public const string ModelNotFound = "MODEL_NOT_FOUND";
    public const string ServerError = "SERVER_ERROR";
    public const string ServerOverloaded = "SERVER_OVERLOADED";
    public const string ParamInvalid = "PARAM_INVALID";
    public const string ContentFiltered = "CONTENT_FILTERED";
    public const string EndpointNotFound = "ENDPOINT_NOT_FOUND";
    public const string UnknownProviderError = "UNKNOWN_PROVIDER_ERROR";
    public const string ParseSseInvalid = "PARSE_SSE_INVALID";
    public const string ParseIncomplete = "PARSE_INCOMPLETE";
    public const string ParseEventTooLarge = "PARSE_EVENT_TOO_LARGE";
    public const string NetConnectionReset = "NET_CONNECTION_RESET";
    public const string NetReadTimeout = "NET_READ_TIMEOUT";
    public const string Internal = "INTERNAL";

    private static readonly Dictionary<string, ErrorSpec> Specs = BuildSpecs();

    public static ErrorSpec Lookup(string code) =>
        Specs.TryGetValue(code, out var spec)
            ? spec
            : throw new ArgumentOutOfRangeException(nameof(code), code, "code is outside the closed set of spec/errors.md §3");

    public static bool IsKnown(string code) => Specs.ContainsKey(code);

    private static Dictionary<string, ErrorSpec> BuildSpecs()
    {
        var specs = new Dictionary<string, ErrorSpec>(StringComparer.Ordinal);

        void Add(string code, string stage, bool retryable, string[] causes, ErrorAction[] actions) =>
            specs[code] = new ErrorSpec(code, stage, retryable, causes, actions);

        // §3.1 network and transport
        Add("NET_OFFLINE", "resolve", true,
            ["errors.NET_OFFLINE.cause.flight_mode", "errors.NET_OFFLINE.cause.network_not_ready", "errors.NET_OFFLINE.cause.vpn_disconnected"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("open_network_settings")]);
        Add("NET_DNS_FAILED", "resolve", true,
            ["errors.NET_DNS_FAILED.cause.base_url_typo", "errors.NET_DNS_FAILED.cause.dns_unavailable", "errors.NET_DNS_FAILED.cause.proxy_required"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.Simple("check_proxy"), ErrorAction.Simple("retry")]);
        Add("NET_CONNECT_TIMEOUT", "connect", true,
            ["errors.NET_CONNECT_TIMEOUT.cause.network_slow", "errors.NET_CONNECT_TIMEOUT.cause.blocked_by_network", "errors.NET_CONNECT_TIMEOUT.cause.proxy_misconfigured"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("check_proxy")]);
        Add("NET_CONNECT_REFUSED", "connect", true,
            ["errors.NET_CONNECT_REFUSED.cause.base_url_port_wrong", "errors.NET_CONNECT_REFUSED.cause.local_service_not_running", "errors.NET_CONNECT_REFUSED.cause.proxy_not_running"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.Simple("check_proxy"), ErrorAction.Simple("retry")]);
        Add("NET_TLS_FAILED", "connect", false,
            ["errors.NET_TLS_FAILED.cause.system_clock_wrong", "errors.NET_TLS_FAILED.cause.proxy_intercepts_certificate", "errors.NET_TLS_FAILED.cause.server_certificate_expired"],
            [ErrorAction.OpenDocs("tls"), ErrorAction.Simple("check_proxy"), ErrorAction.Simple("copy_diagnostics")]);
        Add("NET_PROXY_AUTH_FAILED", "connect", false,
            ["errors.NET_PROXY_AUTH_FAILED.cause.proxy_credentials_wrong", "errors.NET_PROXY_AUTH_FAILED.cause.proxy_scheme_mismatch"],
            [ErrorAction.Simple("check_proxy")]);
        Add("NET_CONNECTION_RESET", "stream_read", true,
            ["errors.NET_CONNECTION_RESET.cause.network_switched", "errors.NET_CONNECTION_RESET.cause.proxy_or_firewall_dropped", "errors.NET_CONNECTION_RESET.cause.server_closed_long_connection"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("check_proxy")]);
        Add("NET_READ_TIMEOUT", "stream_read", true,
            ["errors.NET_READ_TIMEOUT.cause.model_queued", "errors.NET_READ_TIMEOUT.cause.proxy_buffered_response", "errors.NET_READ_TIMEOUT.cause.request_too_large"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("switch_model"), ErrorAction.Simple("check_proxy")]);

        // §3.2 auth and quota
        Add("AUTH_MISSING_KEY", "config", false,
            ["errors.AUTH_MISSING_KEY.cause.never_configured", "errors.AUTH_MISSING_KEY.cause.key_deleted", "errors.AUTH_MISSING_KEY.cause.all_keys_disabled"],
            [ErrorAction.EditProvider("key")]);
        Add(AuthInvalidKey, "provider", false,
            ["errors.AUTH_INVALID_KEY.cause.revoked", "errors.AUTH_INVALID_KEY.cause.wrong_endpoint", "errors.AUTH_INVALID_KEY.cause.truncated_paste"],
            [ErrorAction.EditProvider("key"), ErrorAction.Simple("check_base_url"), ErrorAction.Simple("copy_diagnostics")]);
        Add(AuthInsufficientQuota, "provider", false,
            ["errors.AUTH_INSUFFICIENT_QUOTA.cause.prepaid_balance_exhausted", "errors.AUTH_INSUFFICIENT_QUOTA.cause.free_tier_expired", "errors.AUTH_INSUFFICIENT_QUOTA.cause.org_quota_limited"],
            [ErrorAction.Simple("top_up"), ErrorAction.Simple("switch_model"), ErrorAction.Simple("manage_keys")]);
        Add(RateLimited, "provider", true,
            ["errors.RATE_LIMITED.cause.rpm_exceeded", "errors.RATE_LIMITED.cause.too_many_concurrent", "errors.RATE_LIMITED.cause.daily_quota_exhausted"],
            [ErrorAction.Simple("retry_after"), ErrorAction.Simple("edit_provider"), ErrorAction.Simple("manage_keys")]);

        // §3.3 request and model
        Add(ModelNotFound, "provider", false,
            ["errors.MODEL_NOT_FOUND.cause.model_retired", "errors.MODEL_NOT_FOUND.cause.model_id_typo", "errors.MODEL_NOT_FOUND.cause.base_url_lacks_model"],
            [ErrorAction.Simple("refresh_models"), ErrorAction.Simple("edit_provider")]);
        Add(ParamInvalid, "provider", false,
            ["errors.PARAM_INVALID.cause.param_unsupported", "errors.PARAM_INVALID.cause.value_out_of_range", "errors.PARAM_INVALID.cause.mixed_protocol_fields"],
            [ErrorAction.Simple("view_raw"), ErrorAction.Simple("edit_provider"), ErrorAction.Simple("switch_model")]);
        Add(ContextOverflow, "provider", false,
            ["errors.CONTEXT_OVERFLOW.cause.too_many_messages", "errors.CONTEXT_OVERFLOW.cause.attachment_too_large", "errors.CONTEXT_OVERFLOW.cause.context_window_misconfigured"],
            [ErrorAction.Simple("reduce_context"), ErrorAction.Simple("switch_model"), ErrorAction.Simple("edit_provider")]);
        Add("CAPABILITY_UNSUPPORTED", "config", false,
            ["errors.CAPABILITY_UNSUPPORTED.cause.no_vision", "errors.CAPABILITY_UNSUPPORTED.cause.no_tools", "errors.CAPABILITY_UNSUPPORTED.cause.no_structured_output"],
            [ErrorAction.Simple("switch_model"), ErrorAction.Simple("edit_provider"), ErrorAction.Simple("view_capability_matrix")]);
        Add(ContentFiltered, "provider", false,
            ["errors.CONTENT_FILTERED.cause.input_blocked_by_policy", "errors.CONTENT_FILTERED.cause.output_blocked_midstream", "errors.CONTENT_FILTERED.cause.region_restricted"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("switch_model")]);
        Add(ParseSseInvalid, "parse", false,
            ["errors.PARSE_SSE_INVALID.cause.base_url_not_streaming", "errors.PARSE_SSE_INVALID.cause.proxy_injected_html", "errors.PARSE_SSE_INVALID.cause.endpoint_returned_json_error"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.Simple("check_proxy"), ErrorAction.Simple("view_raw")]);
        Add(ParseIncomplete, "parse", true,
            ["errors.PARSE_INCOMPLETE.cause.network_interrupted", "errors.PARSE_INCOMPLETE.cause.server_closed_early", "errors.PARSE_INCOMPLETE.cause.proxy_truncated"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("check_proxy")]);
        Add(ParseEventTooLarge, "parse", false,
            ["errors.PARSE_EVENT_TOO_LARGE.cause.non_streaming_large_response", "errors.PARSE_EVENT_TOO_LARGE.cause.proxy_duplicated_data"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.Simple("view_raw")]);

        // §3.4 provider side
        Add(ServerError, "provider", true,
            ["errors.SERVER_ERROR.cause.provider_outage", "errors.SERVER_ERROR.cause.model_restarting", "errors.SERVER_ERROR.cause.region_degraded"],
            [ErrorAction.Simple("retry"), ErrorAction.Simple("switch_model"), ErrorAction.Simple("view_service_status")]);
        Add(ServerOverloaded, "provider", true,
            ["errors.SERVER_OVERLOADED.cause.peak_queueing", "errors.SERVER_OVERLOADED.cause.account_downgraded", "errors.SERVER_OVERLOADED.cause.region_capacity"],
            [ErrorAction.Simple("retry_after"), ErrorAction.Simple("switch_model")]);
        Add(EndpointNotFound, "provider", false,
            ["errors.ENDPOINT_NOT_FOUND.cause.base_url_missing_v1", "errors.ENDPOINT_NOT_FOUND.cause.protocol_mismatch", "errors.ENDPOINT_NOT_FOUND.cause.custom_path_required"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.Simple("edit_provider"), ErrorAction.OpenDocs("base_url")]);
        Add(UnknownProviderError, "provider", false,
            ["errors.UNKNOWN_PROVIDER_ERROR.cause.error_format_changed", "errors.UNKNOWN_PROVIDER_ERROR.cause.relay_custom_error", "errors.UNKNOWN_PROVIDER_ERROR.cause.proxy_rewrote_response"],
            [ErrorAction.Simple("view_raw"), ErrorAction.Simple("copy_diagnostics"), ErrorAction.Simple("switch_model")]);

        // §3.5 local
        Add("CONFIG_INVALID_BASE_URL", "config", false,
            ["errors.CONFIG_INVALID_BASE_URL.cause.missing_https_scheme", "errors.CONFIG_INVALID_BASE_URL.cause.illegal_characters", "errors.CONFIG_INVALID_BASE_URL.cause.points_at_web_page"],
            [ErrorAction.Simple("check_base_url"), ErrorAction.OpenDocs("base_url")]);
        Add("KEY_STORE_UNAVAILABLE", "crypto", false,
            ["errors.KEY_STORE_UNAVAILABLE.cause.device_credential_changed", "errors.KEY_STORE_UNAVAILABLE.cause.key_store_reset", "errors.KEY_STORE_UNAVAILABLE.cause.strongbox_unavailable"],
            [ErrorAction.EditProvider("key"), ErrorAction.Simple("copy_diagnostics")]);
        Add("DB_WRITE_FAILED", "persist", false,
            ["errors.DB_WRITE_FAILED.cause.disk_full", "errors.DB_WRITE_FAILED.cause.db_locked_or_corrupt", "errors.DB_WRITE_FAILED.cause.storage_permission_revoked"],
            [ErrorAction.Simple("clean_storage"), ErrorAction.Simple("db_check")]);
        Add("TOOL_EXEC_FAILED", "tool", false,
            ["errors.TOOL_EXEC_FAILED.cause.tool_service_unreachable", "errors.TOOL_EXEC_FAILED.cause.schema_mismatch", "errors.TOOL_EXEC_FAILED.cause.mcp_process_exited"],
            [ErrorAction.Simple("retry_tool"), ErrorAction.OpenDocs("tools"), ErrorAction.Simple("open_mcp_settings")]);
        Add(Internal, "local", false,
            ["errors.INTERNAL.cause.internal_defect", "errors.INTERNAL.cause.inconsistent_state"],
            [ErrorAction.Simple("copy_diagnostics"), ErrorAction.Simple("retry")]);

        // §3.6 import and migration
        Add("IMPORT_MALFORMED", "persist", false,
            ["errors.IMPORT_MALFORMED.cause.not_neochat_export", "errors.IMPORT_MALFORMED.cause.json_truncated", "errors.IMPORT_MALFORMED.cause.nesting_rejected"],
            [ErrorAction.Simple("view_raw"), ErrorAction.OpenDocs("import")]);
        Add("IMPORT_TOO_LARGE", "persist", false,
            ["errors.IMPORT_TOO_LARGE.cause.too_many_attachments", "errors.IMPORT_TOO_LARGE.cause.too_many_conversations", "errors.IMPORT_TOO_LARGE.cause.duplicated_export"],
            [ErrorAction.OpenDocs("import"), ErrorAction.Simple("clean_storage")]);
        Add("IMPORT_UNSUPPORTED_VERSION", "persist", false,
            ["errors.IMPORT_UNSUPPORTED_VERSION.cause.newer_app_version", "errors.IMPORT_UNSUPPORTED_VERSION.cause.format_version_not_1"],
            [ErrorAction.OpenDocs("import"), ErrorAction.Simple("copy_diagnostics")]);

        return specs;
    }
}

public sealed record ErrorSpec(string Code, string Stage, bool Retryable, string[] Causes, ErrorAction[] Actions);
