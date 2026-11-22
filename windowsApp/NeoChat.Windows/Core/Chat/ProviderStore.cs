using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// One provider entry as persisted in <c>providers.json</c>. The key itself never enters this record:
/// <see cref="KeyEnv"/> names the environment variable that holds it (spec/security: no plaintext key
/// on disk), so the file stays shareable and the key stays in the user's environment.
/// </summary>
public sealed record ProviderConfig
{
    public string Id { get; init; } = "default";

    public string Name { get; init; } = "默认提供商";

    public string Protocol { get; init; } = "openai_chat";

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    /// <summary>key_ref: the name of the environment variable that carries this provider's key.</summary>
    [JsonPropertyName("key_env")]
    public string KeyEnv { get; init; } = ProviderStore.DefaultKeyEnv;
}

public sealed record ProviderStoreDocument
{
    public int Version { get; init; } = 1;

    [JsonPropertyName("active_id")]
    public string? ActiveId { get; init; }

    public IReadOnlyList<ProviderConfig> Providers { get; init; } = Array.Empty<ProviderConfig>();
}

/// <summary>
/// Reads and writes <c>%LOCALAPPDATA%\NeoChat\providers.json</c> and resolves the settings a session
/// actually sends with. Precedence is environment over the file, so an unattended run can point the app
/// at another endpoint without touching the stored profile.
/// </summary>
public sealed class ProviderStore
{
    public const string DefaultKeyEnv = "NEOCHAT_API_KEY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<string, string?> _environment;

    public ProviderStore(string directory, Func<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "providers.json");
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    public string FilePath { get; }

    public ProviderStoreDocument Load()
    {
        if (!File.Exists(FilePath))
        {
            return new ProviderStoreDocument();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ProviderStoreDocument>(json, JsonOptions) ?? new ProviderStoreDocument();
        }
        catch (JsonException)
        {
            // A hand-edited file must not stop the app from starting; the caller falls back to defaults.
            return new ProviderStoreDocument();
        }
    }

    /// <summary>Upserts one provider and makes it active.</summary>
    public void Save(ProviderConfig provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var existing = Load();
        var providers = existing.Providers.Where(p => !string.Equals(p.Id, provider.Id, StringComparison.Ordinal)).ToList();
        providers.Add(provider);
        var document = new ProviderStoreDocument { Version = existing.Version, ActiveId = provider.Id, Providers = providers };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    public ProviderConfig? ActiveProvider()
    {
        var document = Load();
        if (document.Providers.Count == 0)
        {
            return null;
        }

        return document.Providers.FirstOrDefault(p => string.Equals(p.Id, document.ActiveId, StringComparison.Ordinal))
            ?? document.Providers[0];
    }

    /// <summary>Environment first, stored profile second; a missing profile leaves the session unconfigured.</summary>
    public ProviderSettings Resolve()
    {
        var provider = ActiveProvider();
        var envBaseUrl = _environment("NEOCHAT_BASE_URL");
        var envModel = _environment("NEOCHAT_MODEL");
        var envKey = _environment("NEOCHAT_API_KEY");

        var baseUrl = !string.IsNullOrWhiteSpace(envBaseUrl) ? envBaseUrl : provider?.BaseUrl ?? string.Empty;
        var model = !string.IsNullOrWhiteSpace(envModel) ? envModel : provider?.Model ?? string.Empty;
        var keyEnvName = provider?.KeyEnv ?? DefaultKeyEnv;
        var apiKey = _environment(keyEnvName) ?? envKey ?? string.Empty;

        return new ProviderSettings
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
            ProtocolLabel = provider?.Protocol is { Length: > 0 } protocol ? protocol : "openai_chat",
        };
    }
}
