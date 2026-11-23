using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>The C# files a generator wrote, parsed for a compilation.</summary>
internal static class GeneratedSources
{
    /// <summary>Every <c>.cs</c> file under <paramref name="directory"/>, parsed, each tree's path its file's.</summary>
    public static async Task<List<SyntaxTree>> ReadAsync(string directory)
    {
        var trees = new List<SyntaxTree>();
        foreach (
            var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
        )
            trees.Add(
                CSharpSyntaxTree.ParseText(
                    await File.ReadAllTextAsync(file).ConfigureAwait(false),
                    path: file
                )
            );
        return trees;
    }
}
