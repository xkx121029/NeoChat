using Xunit;
using System.Text.Json;
using NeoChat.Protocol.Errors;
using NeoChat.Windows.Core.Errors;

namespace NeoChat.Windows.Core.Tests;

public sealed class FailureLogTests
{
    [Fact]
    public void RecordsOneJsonLinePerFailureWithFixTimestamp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "neochat-failure-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new JsonlFailureLog(directory, new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 9, 30, 0, TimeSpan.Zero)));
            log.Record(ProtocolErrors.Create(ErrorCatalog.AuthInvalidKey, ProtocolErrors.NewTraceId(), provider: "openai_chat", model: "m", httpStatus: 401));
            log.Record(ProtocolErrors.Create(ErrorCatalog.NetConnectionReset, ProtocolErrors.NewTraceId(), provider: "openai_chat", model: "m"));

            Assert.Null(log.LastWriteError);
            var lines = File.ReadAllLines(log.Location);
            Assert.Equal(2, lines.Length);

            var first = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal("2026-11-23T09:30:00.0000000+00:00", first.GetProperty("ts").GetString());
            var failed = first.GetProperty("failed");
            Assert.Equal("failed", failed.GetProperty("t").GetString());
            Assert.Equal(ErrorCatalog.AuthInvalidKey, failed.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("errors.AUTH_INVALID_KEY.title", failed.GetProperty("error").GetProperty("title_key").GetString());
            Assert.Equal(401, failed.GetProperty("error").GetProperty("http_status").GetInt32());

            var second = JsonDocument.Parse(lines[1]).RootElement;
            Assert.Equal(ErrorCatalog.NetConnectionReset, second.GetProperty("failed").GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void RedactedPayloadNeverCarriesTheApiKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "neochat-failure-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new JsonlFailureLog(directory);
            log.Record(ProtocolErrors.Create(
                ErrorCatalog.AuthInvalidKey,
                ProtocolErrors.NewTraceId(),
                provider: "openai_chat",
                raw: "{\"error\":{\"message\":\"Incorrect API key provided: sk-abcdefghijklmnopqrstuvwxyz012345\"}}"));

            var content = File.ReadAllText(log.Location);
            Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz012345", content, StringComparison.Ordinal);
            Assert.Contains("sk-a", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
