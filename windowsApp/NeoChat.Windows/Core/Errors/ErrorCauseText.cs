namespace NeoChat.Windows.Core.Errors;

/// <summary>
/// Cause texts for spec/errors.md §3, keyed by the same <c>errors.&lt;CODE&gt;.cause.&lt;slug&gt;</c>
/// keys the protocol layer puts in <c>failed.error.causes</c>. Kept apart from the titles so each file
/// stays well under the 500-line budget of CONTRIBUTING §5.
/// </summary>
internal static class ErrorCauseText
{
    internal static readonly Dictionary<string, string> ByKey = new(StringComparer.Ordinal)
    {
        // §3.1 网络与传输
        ["errors.NET_OFFLINE.cause.flight_mode"] = "飞行模式或无信号",
        ["errors.NET_OFFLINE.cause.network_not_ready"] = "系统网络未就绪",
        ["errors.NET_OFFLINE.cause.vpn_disconnected"] = "VPN 断开",
        ["errors.NET_DNS_FAILED.cause.base_url_typo"] = "Base URL 拼写错误",
        ["errors.NET_DNS_FAILED.cause.dns_unavailable"] = "DNS 被劫持或不可用",
        ["errors.NET_DNS_FAILED.cause.proxy_required"] = "网络需要代理",
        ["errors.NET_CONNECT_TIMEOUT.cause.network_slow"] = "网络过慢",
        ["errors.NET_CONNECT_TIMEOUT.cause.blocked_by_network"] = "被网络环境阻断",
        ["errors.NET_CONNECT_TIMEOUT.cause.proxy_misconfigured"] = "代理配置错误",
        ["errors.NET_CONNECT_REFUSED.cause.base_url_port_wrong"] = "Base URL 端口错误",
        ["errors.NET_CONNECT_REFUSED.cause.local_service_not_running"] = "本地服务（Ollama 等）未启动",
        ["errors.NET_CONNECT_REFUSED.cause.proxy_not_running"] = "代理未运行",
        ["errors.NET_TLS_FAILED.cause.system_clock_wrong"] = "系统时间不正确",
        ["errors.NET_TLS_FAILED.cause.proxy_intercepts_certificate"] = "代理在进行证书拦截",
        ["errors.NET_TLS_FAILED.cause.server_certificate_expired"] = "服务器证书过期",
        ["errors.NET_PROXY_AUTH_FAILED.cause.proxy_credentials_wrong"] = "代理用户名或密码错误",
        ["errors.NET_PROXY_AUTH_FAILED.cause.proxy_scheme_mismatch"] = "代理协议选错（HTTP/SOCKS5）",
        ["errors.NET_CONNECTION_RESET.cause.network_switched"] = "网络切换（Wi-Fi/4G）",
        ["errors.NET_CONNECTION_RESET.cause.proxy_or_firewall_dropped"] = "代理或防火墙中断",
        ["errors.NET_CONNECTION_RESET.cause.server_closed_long_connection"] = "服务端断开长连接",
        ["errors.NET_READ_TIMEOUT.cause.model_queued"] = "模型排队或过载",
        ["errors.NET_READ_TIMEOUT.cause.proxy_buffered_response"] = "代理缓冲了响应",
        ["errors.NET_READ_TIMEOUT.cause.request_too_large"] = "请求过大",

        // §3.2 鉴权与配额
        ["errors.AUTH_MISSING_KEY.cause.never_configured"] = "首次使用未填写",
        ["errors.AUTH_MISSING_KEY.cause.key_deleted"] = "密钥被删除",
        ["errors.AUTH_MISSING_KEY.cause.all_keys_disabled"] = "多个 Key 全部禁用",
        ["errors.AUTH_INVALID_KEY.cause.revoked"] = "密钥被撤销/重置",
        ["errors.AUTH_INVALID_KEY.cause.wrong_endpoint"] = "密钥与端点不匹配（把 A 家的 Key 填进了 B 家）",
        ["errors.AUTH_INVALID_KEY.cause.truncated_paste"] = "粘贴时被截断或含空格",
        ["errors.AUTH_INSUFFICIENT_QUOTA.cause.prepaid_balance_exhausted"] = "预付费余额用尽",
        ["errors.AUTH_INSUFFICIENT_QUOTA.cause.free_tier_expired"] = "免费额度已过期",
        ["errors.AUTH_INSUFFICIENT_QUOTA.cause.org_quota_limited"] = "组织配额被管理员限制",
        ["errors.RATE_LIMITED.cause.rpm_exceeded"] = "每分钟请求数超限",
        ["errors.RATE_LIMITED.cause.too_many_concurrent"] = "并发请求过多",
        ["errors.RATE_LIMITED.cause.daily_quota_exhausted"] = "该 Key 的每日额度用尽",

        // §3.3 请求与模型
        ["errors.MODEL_NOT_FOUND.cause.model_retired"] = "模型已下线或改名",
        ["errors.MODEL_NOT_FOUND.cause.model_id_typo"] = "模型 ID 拼写错误",
        ["errors.MODEL_NOT_FOUND.cause.base_url_lacks_model"] = "该 Base URL 不提供此模型",
        ["errors.PARAM_INVALID.cause.param_unsupported"] = "该模型不支持某个采样参数",
        ["errors.PARAM_INVALID.cause.value_out_of_range"] = "数值越界（如 temperature > 2）",
        ["errors.PARAM_INVALID.cause.mixed_protocol_fields"] = "合并了不同协议所需的字段",
        ["errors.CONTEXT_OVERFLOW.cause.too_many_messages"] = "历史消息太多",
        ["errors.CONTEXT_OVERFLOW.cause.attachment_too_large"] = "附件太大",
        ["errors.CONTEXT_OVERFLOW.cause.context_window_misconfigured"] = "模型窗口被误设",
        ["errors.CAPABILITY_UNSUPPORTED.cause.no_vision"] = "模型不支持图片输入",
        ["errors.CAPABILITY_UNSUPPORTED.cause.no_tools"] = "模型不支持工具调用",
        ["errors.CAPABILITY_UNSUPPORTED.cause.no_structured_output"] = "模型不支持结构化输出",
        ["errors.CONTENT_FILTERED.cause.input_blocked_by_policy"] = "输入触发了服务商策略",
        ["errors.CONTENT_FILTERED.cause.output_blocked_midstream"] = "输出被中途拦截",
        ["errors.CONTENT_FILTERED.cause.region_restricted"] = "账户所在区域限制",
        ["errors.PARSE_SSE_INVALID.cause.base_url_not_streaming"] = "Base URL 指向了非流式端点",
        ["errors.PARSE_SSE_INVALID.cause.proxy_injected_html"] = "代理注入了 HTML 错误页",
        ["errors.PARSE_SSE_INVALID.cause.endpoint_returned_json_error"] = "该端点返回的是 JSON 错误",
        ["errors.PARSE_INCOMPLETE.cause.network_interrupted"] = "网络中断",
        ["errors.PARSE_INCOMPLETE.cause.server_closed_early"] = "服务端提前关闭",
        ["errors.PARSE_INCOMPLETE.cause.proxy_truncated"] = "代理缓冲截断",
        ["errors.PARSE_EVENT_TOO_LARGE.cause.non_streaming_large_response"] = "端点返回了非流式大响应",
        ["errors.PARSE_EVENT_TOO_LARGE.cause.proxy_duplicated_data"] = "代理重复注入数据",

        // §3.4 服务端
        ["errors.SERVER_ERROR.cause.provider_outage"] = "Provider 侧故障",
        ["errors.SERVER_ERROR.cause.model_restarting"] = "模型正在重启",
        ["errors.SERVER_ERROR.cause.region_degraded"] = "该区域服务降级",
        ["errors.SERVER_OVERLOADED.cause.peak_queueing"] = "高峰期排队",
        ["errors.SERVER_OVERLOADED.cause.account_downgraded"] = "账户被降级",
        ["errors.SERVER_OVERLOADED.cause.region_capacity"] = "区域容量不足",
        ["errors.ENDPOINT_NOT_FOUND.cause.base_url_missing_v1"] = "Base URL 缺少或多了 /v1",
        ["errors.ENDPOINT_NOT_FOUND.cause.protocol_mismatch"] = "协议选错（OpenAI 兼容 vs Anthropic）",
        ["errors.ENDPOINT_NOT_FOUND.cause.custom_path_required"] = "该 Path 需要自定义",
        ["errors.UNKNOWN_PROVIDER_ERROR.cause.error_format_changed"] = "该服务商改了错误格式",
        ["errors.UNKNOWN_PROVIDER_ERROR.cause.relay_custom_error"] = "中转服务自定义了错误",
        ["errors.UNKNOWN_PROVIDER_ERROR.cause.proxy_rewrote_response"] = "响应被代理改写",

        // §3.5 本地
        ["errors.CONFIG_INVALID_BASE_URL.cause.missing_https_scheme"] = "缺少 https://",
        ["errors.CONFIG_INVALID_BASE_URL.cause.illegal_characters"] = "含空格或中文标点",
        ["errors.CONFIG_INVALID_BASE_URL.cause.points_at_web_page"] = "指向了网页地址而不是 API 端点",
        ["errors.KEY_STORE_UNAVAILABLE.cause.device_credential_changed"] = "设备锁屏凭据已变更",
        ["errors.KEY_STORE_UNAVAILABLE.cause.key_store_reset"] = "系统密钥库被重置",
        ["errors.KEY_STORE_UNAVAILABLE.cause.strongbox_unavailable"] = "StrongBox 不可用且回退失败",
        ["errors.DB_WRITE_FAILED.cause.disk_full"] = "磁盘空间不足",
        ["errors.DB_WRITE_FAILED.cause.db_locked_or_corrupt"] = "数据库被占用或损坏",
        ["errors.DB_WRITE_FAILED.cause.storage_permission_revoked"] = "存储权限被系统回收",
        ["errors.TOOL_EXEC_FAILED.cause.tool_service_unreachable"] = "工具服务不可达",
        ["errors.TOOL_EXEC_FAILED.cause.schema_mismatch"] = "参数不符合工具 schema",
        ["errors.TOOL_EXEC_FAILED.cause.mcp_process_exited"] = "MCP 服务器进程已退出",
        ["errors.INTERNAL.cause.internal_defect"] = "应用内部缺陷",
        ["errors.INTERNAL.cause.inconsistent_state"] = "数据状态不一致",

        // §3.6 导入与迁移
        ["errors.IMPORT_MALFORMED.cause.not_neochat_export"] = "不是 NeoChat 导出的文件",
        ["errors.IMPORT_MALFORMED.cause.json_truncated"] = "JSON 被截断或损坏",
        ["errors.IMPORT_MALFORMED.cause.nesting_rejected"] = "嵌套过深或字段超长被拒",
        ["errors.IMPORT_TOO_LARGE.cause.too_many_attachments"] = "内嵌附件过多",
        ["errors.IMPORT_TOO_LARGE.cause.too_many_conversations"] = "会话数量过大",
        ["errors.IMPORT_TOO_LARGE.cause.duplicated_export"] = "文件被重复导出叠加",
        ["errors.IMPORT_UNSUPPORTED_VERSION.cause.newer_app_version"] = "文件来自更高版本的 NeoChat",
        ["errors.IMPORT_UNSUPPORTED_VERSION.cause.format_version_not_1"] = "format_version 不是 1",
    };
}
