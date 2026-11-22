using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Events;
using NeoChat.Protocol.Pipeline;
using NeoChat.Protocol.Request;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// HTTP transport for one chat stream. Request shaping stays in the protocol layer
/// (<see cref="OpenAiChatRequestBuilder"/>), SSE parsing and normalization stay in
/// <see cref="ChatStreamSession"/>; this type only performs the send and hands the body over.
/// </summary>
public sealed class HttpChatStreamClient : IChatStreamClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<ProviderSettings> _settings;
    private readonly bool _ownsHttp;
    private readonly Lazy<HttpClient> _direct;

    public HttpChatStreamClient(Func<ProviderSettings> settings, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // 本机 mock / 本地推理服务不能走系统代理：代理会把 loopback 请求也接走并回 404。
        _direct = new Lazy<HttpClient>(() => new HttpClient(
            new HttpClientHandler { UseProxy = false },
            disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = _settings();
        var prepared = OpenAiChatRequestBuilder.Build(
            request,
            new OpenAiChatProvider
            {
                BaseUrl = settings.BaseUrl,
                ApiKey = settings.ApiKey.Length == 0 ? null : settings.ApiKey,
                ProtocolLabel = settings.ProtocolLabel,
            });

        using var message = new HttpRequestMessage(new HttpMethod(prepared.Method), prepared.Uri)
        {
            Content = new ByteArrayContent(prepared.Body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        foreach (var (name, value) in prepared.Headers)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await ClientFor(message.RequestUri)
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var info = new StreamResponseInfo
        {
            StatusCode = (int)response.StatusCode,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            Headers = CollectHeaders(response),
        };
        var options = new ChatStreamOptions
        {
            Protocol = ProtocolFamily.OpenAiChat,
            Provider = settings.ProtocolLabel,
            Model = request.Model,
        };

        using var session = ChatStreamSession.Start(body, info, options);
        await foreach (var streamEvent in session.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }

        if (_direct.IsValueCreated)
        {
            _direct.Value.Dispose();
        }
    }

    private HttpClient ClientFor(Uri? uri) =>
        uri is not null && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            ? _direct.Value
            : _http;

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers)
        {
            headers[name] = string.Join(",", values);
        }

        foreach (var (name, values) in response.Content.Headers)
        {
            headers[name] = string.Join(",", values);
        }

        return headers;
    }
}
