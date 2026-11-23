namespace Trax.Analyzers.Package.Tests;

/// <summary>One line of rules.txt: a rule and the severity it gets in each kind of project.</summary>
internal sealed record RuleLine(string Id, string Src, string Tests);

internal static class RulesFile
{
    public static IReadOnlyList<RuleLine> Read() =>
        [
            .. File.ReadAllLines(Folder.RulesFile)
                .Where(line => line.Trim() is { Length: > 0 } text && !text.StartsWith('#'))
                .Select(line => line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries))
                .Select(parts => new RuleLine(parts[0], parts[1], parts[2])),
        ];
}
