using Xunit;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Tests;

public sealed class ProviderStoreTests
{
    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "neochat-provider-" + Guid.NewGuid().ToString("N"));

    private static Dictionary<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            map[key] = value;
        }

        return map;
    }

    [Fact]
    public void WithoutFileAndWithoutEnvironment_SessionIsUnconfigured()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);

            var settings = store.Resolve();

            Assert.False(settings.IsConfigured);
            Assert.Null(store.ActiveProvider());
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
    public void SavedProvider_RoundTripsWithSnakeCaseFieldsAndNoKeyMaterial()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);
            store.Save(new ProviderConfig
            {
                BaseUrl = "http://127.0.0.1:8787/v1",
                Model = "mock-chat",
                KeyEnv = "MOCK_KEY",
            });

            var json = File.ReadAllText(store.FilePath);
            Assert.Contains("\"base_url\": \"http://127.0.0.1:8787/v1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"key_env\": \"MOCK_KEY\"", json, StringComparison.Ordinal);
            Assert.Contains("\"active_id\": \"default\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("api_key", json, StringComparison.OrdinalIgnoreCase);

            var settings = store.Resolve();
            Assert.True(settings.IsConfigured);
            Assert.Equal("http://127.0.0.1:8787/v1", settings.BaseUrl);
            Assert.Equal("mock-chat", settings.Model);
            Assert.Equal(string.Empty, settings.ApiKey);
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
    public void EnvironmentWinsOverStoredProfile()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);
            store.Save(new ProviderConfig { BaseUrl = "http://stored.invalid/v1", Model = "stored-model" });

            var withEnv = new ProviderStore(
                directory,
                name => name switch
                {
                    "NEOCHAT_BASE_URL" => "http://env.invalid/v1",
                    "NEOCHAT_MODEL" => "env-model",
                    _ => null,
                });

            var settings = withEnv.Resolve();

            Assert.Equal("http://env.invalid/v1", settings.BaseUrl);
            Assert.Equal("env-model", settings.Model);
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
    public void KeyIsReadFromTheDeclaredEnvironmentVariable()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);
            store.Save(new ProviderConfig { BaseUrl = "http://127.0.0.1:8787/v1", Model = "mock-chat", KeyEnv = "MOCK_KEY" });

            var withKey = new ProviderStore(directory, name => name == "MOCK_KEY" ? "k-from-env" : null);

            Assert.Equal("k-from-env", withKey.Resolve().ApiKey);
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
    public void CorruptFile_DoesNotThrowAndFallsBackToUnconfigured()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);
            File.WriteAllText(store.FilePath, "{ this is not json");

            var settings = store.Resolve();

            Assert.False(settings.IsConfigured);
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
    public void SaveReplacesTheSameIdInsteadOfAppending()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProviderStore(directory, _ => null);
            store.Save(new ProviderConfig { BaseUrl = "http://one.invalid/v1", Model = "m1" });
            store.Save(new ProviderConfig { BaseUrl = "http://two.invalid/v1", Model = "m2" });

            Assert.Single(store.Load().Providers);
            Assert.Equal("http://two.invalid/v1", store.Resolve().BaseUrl);
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
