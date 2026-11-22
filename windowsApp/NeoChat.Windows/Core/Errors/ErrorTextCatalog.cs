namespace NeoChat.Windows.Core.Errors;

/// <summary>
/// Consumer text for the closed error code set, transcribed from spec/errors.md §3 (zh-CN column) and
/// §4 (action names). Keys follow §6: <c>errors.&lt;CODE&gt;.title</c> /
/// <c>errors.&lt;CODE&gt;.cause.&lt;slug&gt;</c> / <c>errors.&lt;CODE&gt;.action.&lt;a&gt;</c>.
/// The catalog is intentionally data-only: the protocol layer owns which code fired, this file owns
/// how it reads to a person.
/// </summary>
public static class ErrorTextCatalog
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["NET_OFFLINE"] = "设备当前没有网络连接",
        ["NET_DNS_FAILED"] = "无法解析服务器域名",
        ["NET_CONNECT_TIMEOUT"] = "连接服务器超时",
        ["NET_CONNECT_REFUSED"] = "服务器拒绝了连接",
        ["NET_TLS_FAILED"] = "安全连接建立失败",
        ["NET_PROXY_AUTH_FAILED"] = "代理认证失败",
        ["NET_CONNECTION_RESET"] = "连接被中途断开",
        ["NET_READ_TIMEOUT"] = "服务器长时间没有返回数据",
        ["AUTH_MISSING_KEY"] = "这个 Provider 还没有配置密钥",
        ["AUTH_INVALID_KEY"] = "密钥无效或已被撤销",
        ["AUTH_INSUFFICIENT_QUOTA"] = "账户余额或配额不足",
        ["RATE_LIMITED"] = "请求太频繁，被限流",
        ["MODEL_NOT_FOUND"] = "该模型在服务器上不存在",
        ["PARAM_INVALID"] = "请求参数不被接受",
        ["CONTEXT_OVERFLOW"] = "上下文长度超过模型上限",
        ["CAPABILITY_UNSUPPORTED"] = "该模型不支持此内容类型",
        ["CONTENT_FILTERED"] = "Provider 的内容策略拦截了本次请求或回复",
        ["PARSE_SSE_INVALID"] = "服务器返回的数据格式无法识别",
        ["PARSE_INCOMPLETE"] = "回复在中途被截断",
        ["PARSE_EVENT_TOO_LARGE"] = "单条流数据超出安全上限（16 MiB）",
        ["SERVER_ERROR"] = "服务器内部错误（{http_status}）",
        ["SERVER_OVERLOADED"] = "服务器过载，暂时无法处理",
        ["ENDPOINT_NOT_FOUND"] = "端点地址不存在（404）",
        ["UNKNOWN_PROVIDER_ERROR"] = "Provider 返回了未识别的错误",
        ["CONFIG_INVALID_BASE_URL"] = "Base URL 格式不正确",
        ["KEY_STORE_UNAVAILABLE"] = "无法访问系统密钥库",
        ["DB_WRITE_FAILED"] = "数据保存失败",
        ["TOOL_EXEC_FAILED"] = "工具执行失败",
        ["INTERNAL"] = "出现了未预期的内部错误",
        ["IMPORT_MALFORMED"] = "导入文件结构不合法",
        ["IMPORT_TOO_LARGE"] = "导入文件超出大小上限",
        ["IMPORT_UNSUPPORTED_VERSION"] = "该文件版本较新，当前应用无法导入",
    };

    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        ["retry"] = "重试",
        ["retry_after"] = "稍后重试",
        ["edit_provider"] = "编辑提供商",
        ["open_provider_settings"] = "提供商设置",
        ["manage_keys"] = "管理密钥",
        ["check_proxy"] = "检查代理",
        ["check_base_url"] = "检查 Base URL",
        ["switch_model"] = "切换模型",
        ["reduce_context"] = "精简上下文",
        ["top_up"] = "去充值",
        ["open_docs"] = "查看帮助",
        ["view_raw"] = "查看原始响应",
        ["copy_diagnostics"] = "复制诊断",
        ["retry_tool"] = "重试工具",
        ["db_check"] = "数据库自检",
        ["open_network_settings"] = "网络设置",
        ["refresh_models"] = "刷新模型",
        ["clean_storage"] = "清理存储",
        ["view_capability_matrix"] = "能力矩阵",
        ["view_service_status"] = "服务状态",
        ["open_mcp_settings"] = "MCP 设置",
    };

    /// <summary>Title for a code, with <c>{param}</c> placeholders filled from the error object.</summary>
    public static string Title(string code, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var template = Titles.TryGetValue(code, out var known) ? known : code;
        foreach (var (key, value) in parameters)
        {
            template = template.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return template
            .Replace("（）", string.Empty, StringComparison.Ordinal)
            .Replace("（)", string.Empty, StringComparison.Ordinal);
    }

    public static string Cause(string key) =>
        ErrorCauseText.ByKey.TryGetValue(key, out var text) ? text : "未在文案表中登记的原因";

    public static string Action(string code) =>
        Actions.TryGetValue(code, out var label) ? label : code;

    public static bool IsKnownAction(string code) => Actions.ContainsKey(code);
}
