using System.Text;
using System.Text.RegularExpressions;

namespace NeoChat.Protocol.Security;

/// <summary>
/// The redaction pipeline of spec/errors.md §7. The order is normative and must not be reordered:
/// structural fields, then key-shaped tokens, then emails and local paths, and only then the
/// 512-byte UTF-8 cut (redacting after truncation would leave half a key behind).
/// </summary>
public static partial class Redactor
{
    public const int RawMaxBytes = 512;

    private const string Redacted = "[redacted]";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var step1 = StructuralFieldRegex().Replace(
            value,
            match => match.Groups["name"].Value + match.Groups["sep"].Value + Redacted);
        step1 = QuerySecretRegex().Replace(
            step1,
            match => match.Groups["pre"].Value + match.Groups["name"].Value + "=" + Redacted);
        var step2 = ProviderKeyRegex().Replace(step1, match => Shorten(match.Value));
        var step3 = EmailRegex().Replace(step2, match => MaskEmail(match.Value));
        step3 = UserPathRegex().Replace(step3, match => match.Groups["root"].Value + "~");
        return TruncateUtf8(step3, RawMaxBytes);
    }

    /// <summary>Truncates on a UTF-8 character boundary so no multi-byte character is split.</summary>
    public static string TruncateUtf8(string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "byte budget may not be negative");
        }

        var bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes <= maxBytes)
        {
            return value;
        }

        var used = 0;
        var cut = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (used + size > maxBytes)
            {
                break;
            }

            used += size;
            cut += rune.Utf16SequenceLength;
        }

        return value[..cut];
    }

    private static string Shorten(string key) =>
        key.Length <= 8 ? Redacted : string.Concat(key.AsSpan(0, 4), "…", key.AsSpan(key.Length - 4));

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return email;
        }

        return string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    [GeneratedRegex(
        @"(?<name>authorization|x-api-key|api-key|cookie|set-cookie)(?<sep>[""']?\s*[:=]\s*[""']?)[^""'\r\n,;}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StructuralFieldRegex();

    [GeneratedRegex(
        @"(?<pre>[?&;])(?<name>api_key|access_token|key)=[^&\s""#]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(
        @"sk-ant-[A-Za-z0-9_-]{16,}|sk-[A-Za-z0-9_-]{16,}|AIza[0-9A-Za-z_-]{20,}|gsk_[A-Za-z0-9]{20,}|xai-[A-Za-z0-9]{16,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKeyRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<root>[A-Za-z]:\\Users\\|/home/|/Users/)[^\\/\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex UserPathRegex();
}
