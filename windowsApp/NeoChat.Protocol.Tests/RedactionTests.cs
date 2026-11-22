using System.Text;
using NeoChat.Protocol.Security;

namespace NeoChat.Protocol.Tests;

/// <summary>spec/errors.md §7: the redaction pipeline and its fixed order.</summary>
public sealed class RedactionTests
{
    [Fact]
    public void AuthorizationHeaderIsReplacedWholesale()
    {
        var redacted = Redactor.Redact("GET /v1/models\nAuthorization: Bearer sk-abcdefghijklmnopqrstuvwxyz012345\nHost: api.example.com");

        Assert.Contains("Authorization: [redacted]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz012345", redacted, StringComparison.Ordinal);
        Assert.Contains("Host: api.example.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void XApiKeyHeaderIsReplacedWholesale()
    {
        var redacted = Redactor.Redact("{\"x-api-key\":\"plainsecretvalue123\",\"body\":1}");

        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("plainsecretvalue123", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void QuerySecretsAreReplacedWholesale()
    {
        var redacted = Redactor.Redact("https://example.com/v1/models?key=abc123&model=x&access_token=tok");

        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.Contains("access_token=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("?key=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("model=x", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sk-abcdefghijklmnopqrstuv", "sk-a…stuv")]
    [InlineData("AIzaSyA1234567890abcdefghijklmn", "AIza…klmn")]
    [InlineData("gsk_1234567890abcdefghijklmnop", "gsk_…mnop")]
    [InlineData("xai-1234567890abcdefghijkl", "xai-…ijkl")]
    public void ProviderKeyShapesKeepFirstFourAndLastFour(string key, string expected)
    {
        var redacted = Redactor.Redact("Incorrect API key provided: " + key);

        Assert.Contains(expected, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(key, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailsAndUserPathsAreMasked()
    {
        var redacted = Redactor.Redact(@"org linch.x@example.com wrote C:\Users\linch\AppData\cache.txt and /home/linch/.config/neochat");

        Assert.Contains("l***@example.com", redacted, StringComparison.Ordinal);
        Assert.Contains(@"C:\Users\~\AppData", redacted, StringComparison.Ordinal);
        Assert.Contains("/home/~/", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("linch.x@", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactionRunsBeforeTruncation_SoNoHalfKeySurvives()
    {
        var key = "sk-ABCDEFGHIJKLMNOPQRSTUVWX";
        var input = new string('x', 500) + " " + key + " tail";

        var redacted = Redactor.Redact(input);

        Assert.True(Encoding.UTF8.GetByteCount(redacted) <= Redactor.RawMaxBytes, $"raw is {Encoding.UTF8.GetByteCount(redacted)} bytes");
        Assert.DoesNotContain(key, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ABCDEFGHIJ", redacted, StringComparison.Ordinal);
        Assert.Contains("sk-A…UVWX", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncationNeverSplitsAMultibyteCharacter()
    {
        var input = string.Concat(Enumerable.Repeat("中", 400));

        var redacted = Redactor.Redact(input);

        Assert.True(Encoding.UTF8.GetByteCount(redacted) <= Redactor.RawMaxBytes);
        Assert.DoesNotContain('\uFFFD', redacted);
        Assert.Equal(170, redacted.Length);
    }
}
