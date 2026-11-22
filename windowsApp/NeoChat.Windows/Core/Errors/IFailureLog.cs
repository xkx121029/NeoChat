namespace NeoChat.Windows.Core.Errors;

/// <summary>
/// Durable record of every failure the user saw (spec/errors.md §2 object, wire shape preserved).
/// One JSON line per failure, appended under the application's local folder.
/// </summary>
public interface IFailureLog
{
    void Record(NeoChat.Protocol.Events.ProviderError providerError);

    /// <summary>Absolute path of the file being appended, for the diagnostics surface and for tests.</summary>
    string Location { get; }
}
