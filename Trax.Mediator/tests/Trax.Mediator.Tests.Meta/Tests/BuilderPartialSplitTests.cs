namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// Each builder has a state file, and its Build() lives in the .Build partial.
///
/// <para>Not ADR-enforcing: the partial-class split is a pattern documented at reference/builder-pattern, not a decision taken against alternatives.</para>
/// </summary>
[TestFixture]
public class BuilderPartialSplitTests
{
    private static readonly Regex BuildMethod = new(
        @"^\s*(public|internal|protected|private)\s+(?:[\w<>\.]+\s+)?Build\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly HashSet<string> KnownExceptions = new(StringComparer.Ordinal);

    public static IEnumerable<TestCaseData> BuilderDirectories()
    {
        var configRoot = RepoRoot.Combine("src");
        if (!Directory.Exists(configRoot))
            yield break;
        foreach (
            var configDir in Directory.EnumerateDirectories(
                configRoot,
                "Configuration",
                SearchOption.AllDirectories
            )
        )
        {
            foreach (var dir in Directory.EnumerateDirectories(configDir))
            {
                var name = Path.GetFileName(dir);
                if (!name.EndsWith("Builder", StringComparison.Ordinal))
                    continue;
                yield return new TestCaseData(name, dir).SetName($"Builder({name})");
            }
        }
    }

    [TestCaseSource(nameof(BuilderDirectories))]
    public void Builder_HasStateFile(string builderName, string directoryPath)
    {
        var stateFile = Path.Combine(directoryPath, $"{builderName}.cs");
        File.Exists(stateFile)
            .Should()
            .BeTrue(
                $"every '<Name>Builder/' directory must contain a '<Name>Builder.cs' file holding the "
                    + "state and constructor. Trax.Docs/reference/builder-pattern.md > Class "
                    + $"structure. Missing: '{RepoRoot.Relative(stateFile)}'."
            );
    }

    [TestCaseSource(nameof(BuilderDirectories))]
    public void BuildMethod_LivesIn_DotBuildFile(string builderName, string directoryPath)
    {
        if (KnownExceptions.Contains(builderName))
            Assert.Pass($"'{builderName}' is grandfathered in.");

        var dotBuildFile = $"{builderName}.Build.cs";
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.cs"))
        {
            var content = File.ReadAllText(file);
            var stripped = SourceText.StripCommentsAndStrings(content);
            if (!BuildMethod.IsMatch(stripped))
                continue;

            var fileName = Path.GetFileName(file);
            if (fileName.Equals(dotBuildFile, StringComparison.Ordinal))
                continue;

            found.Add(RepoRoot.Relative(file));
        }

        found
            .Should()
            .BeEmpty(
                $"the Build() method for '{builderName}' must live in '{dotBuildFile}', not in another "
                    + "file. Trax.Docs/reference/builder-pattern.md > Build() requires this split. "
                    + "Found Build() in:\n  "
                    + string.Join("\n  ", found)
            );
    }
}
