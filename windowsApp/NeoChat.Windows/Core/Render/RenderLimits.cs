namespace NeoChat.Windows.Core.Render;

/// <summary>§5 上限与截断. Limits are contract values from the spec, not design tokens.</summary>
public static class RenderLimits
{
    /// <summary>§5 总输入字符 200,000.</summary>
    public const int MaxInputChars = 200_000;

    /// <summary>§5 单消息块数 5,000.</summary>
    public const int MaxBlocks = 5_000;

    /// <summary>§5 表格列数 60 — 超出部分列丢弃.</summary>
    public const int MaxTableColumns = 60;

    /// <summary>§5 表格行数 5,000.</summary>
    public const int MaxTableRows = 5_000;

    /// <summary>§5 嵌套深度（list/quote）32 — 超出降级为 unknown.</summary>
    public const int MaxContainerDepth = 32;

    /// <summary>§3 内联嵌套 8 — 超出降级为 text.</summary>
    public const int MaxInlineDepth = 8;

    /// <summary>§5 代码块行数 20,000.</summary>
    public const int MaxCodeLines = 20_000;
}

/// <summary>
/// §7 链接协议白名单: <c>http</c>, <c>https</c>, <c>mailto</c>. Everything else (javascript:, data:,
/// file:, custom schemes) must render as non-clickable text with a UI hint, so the renderer asks this
/// before it builds a hyperlink; the parser keeps the destination untouched for cross-platform equality.
/// </summary>
public static class LinkSafety
{
    /// <summary>Image sources: <c>http(s)</c> and local attachments only.</summary>
    public static bool IsAllowedImageSource(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        var trimmed = src.Trim();
        return StartsWithScheme(trimmed, "http://") ||
               StartsWithScheme(trimmed, "https://") ||
               StartsWithScheme(trimmed, "attachment://");
    }

    public static bool IsAllowedLinkDestination(string? dest)
    {
        if (string.IsNullOrWhiteSpace(dest))
        {
            return false;
        }

        var trimmed = dest.Trim();
        if (trimmed.StartsWith('#'))
        {
            return true;
        }

        return StartsWithScheme(trimmed, "http://") ||
               StartsWithScheme(trimmed, "https://") ||
               StartsWithScheme(trimmed, "mailto:");
    }

    /// <summary>True when the destination names a scheme at all (so a relative path is not a scheme).</summary>
    public static bool HasScheme(string dest)
    {
        var value = dest.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == ':')
            {
                return index > 0;
            }

            var isSchemeChar = ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' ||
                               (index > 0 && (ch is >= '0' and <= '9' or '+' or '-' or '.'));
            if (!isSchemeChar)
            {
                return false;
            }
        }

        return false;
    }

    private static bool StartsWithScheme(string value, string scheme) =>
        value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase);
}
