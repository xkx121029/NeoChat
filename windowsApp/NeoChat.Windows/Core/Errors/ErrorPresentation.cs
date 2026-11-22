using NeoChat.Protocol.Events;

namespace NeoChat.Windows.Core.Errors;

/// <summary>One clickable banner action, already localized (spec/errors.md §4 closed set).</summary>
public sealed record ErrorActionItem(string Code, string Label, string? Topic, int? RetryAfterMs);

/// <summary>
/// The three parts every error must show (spec/errors.md preamble): what happened, the most likely
/// causes in order, and the next step. Built once per failure and handed to the banner unchanged.
/// </summary>
public sealed record ErrorPresentation
{
    private ErrorPresentation(
        string code,
        string title,
        IReadOnlyList<string> causes,
        IReadOnlyList<ErrorActionItem> actions,
        string traceId,
        string? raw)
    {
        Code = code;
        Title = title;
        Causes = causes;
        Actions = actions;
        TraceId = traceId;
        Raw = raw;
    }

    public string Code { get; }

    public string Title { get; }

    public IReadOnlyList<string> Causes { get; }

    public IReadOnlyList<ErrorActionItem> Actions { get; }

    public string TraceId { get; }

    public string? Raw { get; }

    public bool CanRetry => Actions.Any(a => a.Code is "retry" or "retry_after");

    public static ErrorPresentation From(ProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var causes = error.Causes.Select(ErrorTextCatalog.Cause).Take(3).ToArray();
        var actions = error.Actions
            .Select(a => new ErrorActionItem(a.A, ErrorTextCatalog.Action(a.A), a.Topic, a.Ms))
            .ToArray();
        return new ErrorPresentation(
            error.Code,
            ErrorTextCatalog.Title(error.Code, error.Params),
            causes,
            actions,
            error.TraceId,
            error.Raw);
    }

    /// <summary>Diagnostics block for the clipboard:脱敏 fields only, never the request or the key.</summary>
    public string ToDiagnosticsText() =>
        $"code={Code}{Environment.NewLine}trace_id={TraceId}{Environment.NewLine}raw={Raw ?? "-"}";
}
