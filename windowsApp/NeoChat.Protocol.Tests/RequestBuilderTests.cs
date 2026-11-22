using System.Text.Json.Nodes;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Json;
using NeoChat.Protocol.Request;

namespace NeoChat.Protocol.Tests;

/// <summary>spec/providers.md §3.2: the OpenAI Chat Completions request the builder produces.</summary>
public sealed class RequestBuilderTests
{
    private static ChatRequest Minimal() => new()
    {
        Model = "deepseek-chat",
        System = "You are NeoChat.",
        Messages =
        [
            new ChatMessage("user", [new TextPart("你好")]),
        ],
        Sampling = new Sampling { Temperature = 0.7, TopP = 1.0, MaxOutputTokens = 2048 },
    };

    private static OpenAiChatProvider Provider(params (string Key, string Value)[] headers) => new()
    {
        BaseUrl = "https://api.example.com/v1",
        ApiKey = "sk-test-0123456789abcdef",
        ExtraHeaders = headers.Length == 0 ? null : headers.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void BodyCarriesTheStreamContract()
    {
        var prepared = OpenAiChatRequestBuilder.Build(Minimal(), Provider());

        var body = JsonNode.Parse(prepared.BodyJson)!.AsObject();
        Assert.Equal("POST", prepared.Method);
        Assert.Equal("deepseek-chat", body["model"]!.GetValue<string>());
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.True(body["stream_options"]!["include_usage"]!.GetValue<bool>());
        Assert.Equal(0.7, body["temperature"]!.GetValue<double>());
        Assert.Equal(1.0, body["top_p"]!.GetValue<double>());
        Assert.Equal(2048, body["max_tokens"]!.GetValue<int>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("You are NeoChat.", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("你好", body["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void BodyJsonIsCanonical_SortedAndCompact()
    {
        var prepared = OpenAiChatRequestBuilder.Build(Minimal(), Provider());

        Assert.Equal(
            "{\"max_tokens\":2048,\"messages\":[{\"content\":\"You are NeoChat.\",\"role\":\"system\"},{\"content\":\"你好\",\"role\":\"user\"}],\"model\":\"deepseek-chat\",\"stream\":true,\"stream_options\":{\"include_usage\":true},\"temperature\":0.7,\"top_p\":1}",
            prepared.BodyJson);
        Assert.Equal(CanonicalJson.Serialize(JsonNode.Parse(prepared.BodyJson)), prepared.BodyJson);
    }

    [Fact]
    public void HeadersCarryBearerTokenAndStreamAccept()
    {
        var prepared = OpenAiChatRequestBuilder.Build(Minimal(), Provider());

        Assert.Equal("Bearer sk-test-0123456789abcdef", prepared.Headers["Authorization"]);
        Assert.Equal("application/json", prepared.Headers["Content-Type"]);
        Assert.Equal("text/event-stream", prepared.Headers["Accept"]);
        Assert.StartsWith("NeoChat/", prepared.Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public void ExtraHeaderReplacesAuthorization()
    {
        var prepared = OpenAiChatRequestBuilder.Build(Minimal(), Provider(("x-api-key", "gateway-key")));

        Assert.False(prepared.Headers.ContainsKey("Authorization"));
        Assert.Equal("gateway-key", prepared.Headers["x-api-key"]);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://proxy.local/v1/chat/completions", "https://proxy.local/v1/chat/completions")]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1/chat/completions")]
    public void PathJoiningFollowsProviderRules(string baseUrl, string expected)
    {
        Assert.Equal(expected, OpenAiChatRequestBuilder.ResolveChatCompletionsUrl(baseUrl));

        var prepared = OpenAiChatRequestBuilder.Build(Minimal(), new OpenAiChatProvider { BaseUrl = baseUrl, ApiKey = "k" });
        Assert.Equal(expected, prepared.Uri);
    }

    [Theory]
    [InlineData("api.openai.com/v1")]
    [InlineData("ftp://api.openai.com/v1")]
    [InlineData("")]
    public void InvalidBaseUrlIsRejected(string baseUrl)
    {
        Assert.ThrowsAny<ArgumentException>(() => OpenAiChatRequestBuilder.ResolveChatCompletionsUrl(baseUrl));
    }

    [Fact]
    public void CustomExtraQueryIsAppendedInSortedOrder()
    {
        var request = Minimal() with
        {
            Extra = new ExtraPayload { Query = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" } },
        };

        var prepared = OpenAiChatRequestBuilder.Build(request, Provider());

        Assert.Equal("https://api.example.com/v1/chat/completions?a=first&z=last", prepared.Uri);
    }

    [Fact]
    public void MaxCompletionTokensModeUsesTheNewField()
    {
        var provider = Provider() with { UseMaxCompletionTokens = true };

        var body = JsonNode.Parse(OpenAiChatRequestBuilder.Build(Minimal(), provider).BodyJson)!.AsObject();

        Assert.Equal(2048, body["max_completion_tokens"]!.GetValue<int>());
        Assert.False(body.ContainsKey("max_tokens"));
    }

    [Fact]
    public void ToolsToolChoiceAndReasoningEffortAreMapped()
    {
        var request = Minimal() with
        {
            Messages =
            [
                new ChatMessage("user", [new TextPart("查天气")]),
                new ChatMessage("assistant", [new ToolCallPart("call_a1", "web_search", JsonNode.Parse("{\"q\":\"猫\"}"))]),
                new ChatMessage("tool", [new ToolResultPart(true, JsonNode.Parse("{\"items\":[]}"))], ToolCallId: "call_a1"),
            ],
            Tools = [new ToolDefinition("web_search", "搜索", JsonNode.Parse("{\"type\":\"object\"}"))],
            ToolChoice = "auto",
            Reasoning = new Reasoning { Mode = ReasoningMode.Effort, Effort = ReasoningEffort.High },
        };

        var body = JsonNode.Parse(OpenAiChatRequestBuilder.Build(request, Provider()).BodyJson)!.AsObject();

        Assert.Equal("function", body["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("web_search", body["tools"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("auto", body["tool_choice"]!.GetValue<string>());
        Assert.Equal("high", body["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("{\"q\":\"猫\"}", body["messages"]![2]!["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
        Assert.Equal("call_a1", body["messages"]![3]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("{\"items\":[]}", body["messages"]![3]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void UnsupportedTopKIsStrippedWithANotice()
    {
        var request = Minimal() with { Sampling = new Sampling { Temperature = 0.2, TopK = 40 } };

        var prepared = OpenAiChatRequestBuilder.Build(request, Provider());

        Assert.DoesNotContain("top_k", prepared.BodyJson, StringComparison.Ordinal);
        var notice = Assert.Single(prepared.Notices);
        Assert.Equal(NoticeCode.CapabilityStripped, notice.Code);
        Assert.Equal("top_k", notice.Parameters!["dropped"][0]);
    }

    [Fact]
    public void BudgetReasoningConvertsToEffortByTheReverseTable()
    {
        foreach (var (budget, expected) in new[] { (1024, "low"), (4096, "medium"), (16384, "high") })
        {
            var request = Minimal() with { Reasoning = new Reasoning { Mode = ReasoningMode.Budget, BudgetTokens = budget } };
            var body = JsonNode.Parse(OpenAiChatRequestBuilder.Build(request, Provider()).BodyJson)!.AsObject();
            Assert.Equal(expected, body["reasoning_effort"]!.GetValue<string>());
        }
    }
}
