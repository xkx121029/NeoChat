using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NeoChat.Domain.Tests;

public sealed class LayerBoundaryTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.UI",
        "Windows.UI",
        "NeoChat.Windows",
        "System.Net.Http",
        "Windows.Networking",
    ];

    [Fact]
    public void DomainSourcesDoNotReferenceUiOrPlatformNamespaces()
    {
        var root = LocateDomainSources();
        var sources = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);

        Assert.True(sources.Length > 0, $"领域层源文件数为 0，扫描无意义：{root}");

        var violations = new List<string>();
        foreach (var file in sources)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimStart();
                if (!line.StartsWith("using ", StringComparison.Ordinal))
                {
                    continue;
                }

                var imported = line["using ".Length..].Trim().TrimEnd(';');
                var hit = ForbiddenNamespacePrefixes.FirstOrDefault(
                    prefix => imported.StartsWith(prefix, StringComparison.Ordinal));
                if (hit is not null)
                {
                    violations.Add($"{file}:{index + 1} 引用 {imported}（禁止前缀 {hit}）");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "领域层禁止引用 UI/平台/具体 provider：\n" + string.Join("\n", violations));
    }

    private static string LocateDomainSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "NeoChat.Domain");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 windowsApp/NeoChat.Domain 目录");
    }
}
