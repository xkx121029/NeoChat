using Xunit;
using System.Diagnostics;
using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// §4 streaming contract: never throws, prefix stability (§4.5), block ids stable (§4.6), incremental
/// result identical to a full parse of the same prefix, and append cost independent of document size (§4.7).
/// </summary>
public sealed class RenderStreamTests
{
    /// <summary>200 characters of mixed Markdown: heading, emphasis, code span, list, quote, fence, table, math, footnote.</summary>
    private const string Mixed =
        "# 流式标题\n\n" +
        "这段**粗体**里有 `code` 与 [链接](https://example.com)，还有 $x^2$ 与脚注[^1]。\n\n" +
        "- 列表一\n- 列表二\n\n" +
        "> 引用\n\n" +
        "```csharp\nvar a = 1;\n```\n\n" +
        "| a | b |\n| --- | ---: |\n| 1 | 2 |\n\n" +
        "[^1]: 注";

    [Fact]
    public void PrefixStability_HoldsAcrossEverySingleCharacterAppend()
    {
        var parser = new IncrementalRenderParser();
        var states = new List<RenderDocument>(Mixed.Length);
        for (var length = 1; length <= Mixed.Length; length++)
        {
            states.Add(parser.Update(Mixed[..length]));
        }

        var final = parser.Finish();
        var violations = new List<string>();

        foreach (var state in states)
        {
            // §4.5: every block except the last one must already equal the final model.
            var frozen = Math.Max(0, state.Blocks.Count - 1);
            for (var index = 0; index < frozen && index < final.Blocks.Count; index++)
            {
                var seen = RenderModelJson.SerializeBlock(state.Blocks[index]);
                var expected = RenderModelJson.SerializeBlock(final.Blocks[index]);
                if (!string.Equals(seen, expected, StringComparison.Ordinal))
                {
                    violations.Add($"block {index} drifted: {seen} != {expected}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations.Take(5)));
        Assert.Equal(final.Blocks.Count, states[^1].Blocks.Count);
    }

    [Fact]
    public void Incremental_MatchesFullParseAtEveryPrefix()
    {
        var parser = new IncrementalRenderParser();
        for (var length = 1; length <= Mixed.Length; length++)
        {
            var prefix = Mixed[..length];
            var incremental = parser.Update(prefix).Blocks;
            var full = RenderMarkdown.Parse(prefix).Blocks;
            Assert.Equal(full.Count, incremental.Count);
            for (var index = 0; index < full.Count; index++)
            {
                // Flags differ by design (a stream is incomplete); the blocks must not.
                Assert.Equal(RenderModelJson.SerializeBlock(full[index]), RenderModelJson.SerializeBlock(incremental[index]));
            }
        }
    }

    [Fact]
    public void UnterminatedFence_NeverThrowsAndKeepsBlockIdStable()
    {
        var parser = new IncrementalRenderParser();

        var open = parser.Update("说明\n\n```python\nprint(1)");
        var code = Assert.IsType<CodeBlock>(open.Blocks[^1]);
        Assert.False(code.Closed);
        Assert.Equal("b1", code.Id);
        Assert.True(open.Flags.Incomplete);

        var closed = parser.Update("说明\n\n```python\nprint(1)\nprint(2)\n```\n");
        var closedCode = Assert.IsType<CodeBlock>(closed.Blocks[^1]);
        Assert.True(closedCode.Closed);
        Assert.Equal("b1", closedCode.Id);
        Assert.Equal(2, closed.Blocks.Count);
    }

    [Fact]
    public void UnterminatedMathBlock_SwitchesInPlaceWhenClosed()
    {
        var parser = new IncrementalRenderParser();

        var open = parser.Update("$$\\na^2");
        var unclosed = Assert.IsType<MathBlock>(Assert.Single(open.Blocks));
        Assert.False(unclosed.Closed);

        var closed = parser.Update("$$\\na^2 + b^2\n$$");
        var math = Assert.IsType<MathBlock>(Assert.Single(closed.Blocks));
        Assert.True(math.Closed);
        Assert.Equal("b0", math.Id);
    }

    [Fact]
    public void TableRows_GrowWithoutRewritingTheTableBlock()
    {
        var parser = new IncrementalRenderParser();
        parser.Update("| a | b |\n| --- | --- |\n| 1 | 2 |");

        var document = parser.Update("| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |\n| 5 | 6 |");

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("b0", table.Id);
    }

    [Fact]
    public void RewrittenHistory_ReparsesCleanly()
    {
        var parser = new IncrementalRenderParser();
        parser.Update("# 标题\n\n旧的正文");

        var document = parser.Update("# 标题\n\n新的正文");

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[^1]);
        Assert.Equal("新的正文", Assert.IsType<TextInline>(Assert.Single(paragraph.Inlines)).Text);
    }

    [Fact]
    public void MalformedInput_NeverThrows()
    {
        var cases = new[]
        {
            "```",
            "~~~",
            "$$",
            "\\[",
            "> > > > ",
            "- ",
            "1. ",
            "|",
            "| a |",
            "| --- |",
            "[^]",
            "[a]: ",
            "<div",
            "***",
            "\\",
            "![",
            "[a](b",
            "\u0000\u001f",
            new string('*', 500),
            new string('#', 500),
            new string('-', 500),
        };

        foreach (var markdown in cases)
        {
            var document = RenderMarkdown.Parse(markdown);
            Assert.NotNull(document);
            _ = RenderModelJson.Serialize(document);
        }
    }

    private static int _debugBlocks;

    [Fact]
    public void AppendTenCharacters_StaysFarBelowTheFloorBudget()
    {
        var builder = new System.Text.StringBuilder();
        while (builder.Length < 40_000)
        {
            builder.Append(Mixed).Append("\n\n");
        }

        var parser = new IncrementalRenderParser();
        var warm = parser.Update(builder.ToString());
        _debugBlocks = warm.Blocks.Count;

        var lengths = new List<double>(120);
        for (var step = 0; step < 120; step++)
        {
            var text = builder.ToString() + "\n额外的十个字符追加" + step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.Clear();
            builder.Append(text);

            var stopwatch = Stopwatch.StartNew();
            var doc = parser.Update(text);
            stopwatch.Stop();
            lengths.Add(stopwatch.Elapsed.TotalMilliseconds);
            _debugBlocks = doc.Blocks.Count;
        }

        lengths.Sort();
        var median = lengths[lengths.Count / 2];
        var floor = 4.0;
        Assert.True(median <= floor, $"median append-10 was {median:F3}ms (min {lengths[0]:F3}, max {lengths[^1]:F3}), floor is {floor}ms, {_debugBlocks} blocks");
    }
}
