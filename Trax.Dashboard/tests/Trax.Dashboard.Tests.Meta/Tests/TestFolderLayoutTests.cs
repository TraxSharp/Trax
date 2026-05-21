namespace Trax.Dashboard.Tests.Meta.Tests;

[TestFixture]
public class TestFolderLayoutTests
{
    /// <summary>
    /// Folder names that are explicitly forbidden as direct children of a test project.
    /// (TestResults/ and coverage/ are gitignored and excluded here; a separate test verifies
    /// that they remain gitignored.)
    /// </summary>
    private static readonly HashSet<string> ForbiddenFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vs",
        ".idea",
        "node_modules",
        "Junk",
        "Tmp",
        "Temp",
        "Misc",
        "Old",
        "Legacy",
    };

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "TestResults",
        "coverage",
    };

    private static readonly Regex PascalCaseFolder = new(
        @"^[A-Z][A-Za-z0-9]*$",
        RegexOptions.Compiled
    );

    private static readonly HashSet<string> LowercaseExceptions = new(StringComparer.Ordinal)
    {
        "tests",
    };

    [Test]
    public void TestProjects_DoNotContain_ForbiddenFolders()
    {
        var offenders = new List<string>();

        var testsRoot = RepoRoot.Combine("tests");
        if (!Directory.Exists(testsRoot))
            Assert.Inconclusive("No tests/ directory in this repo.");

        foreach (var projectDir in Directory.EnumerateDirectories(testsRoot))
        {
            foreach (var sub in Directory.EnumerateDirectories(projectDir))
            {
                var name = Path.GetFileName(sub);
                if (ForbiddenFolders.Contains(name))
                    offenders.Add($"{RepoRoot.Relative(sub)} (forbidden folder '{name}')");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Test projects must not contain anti-pattern folder names. "
                    + "'Junk/'/'Misc/'/'Tmp/'/'Old/' signal poor organization. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void TestProjects_TopLevelFolders_Are_PascalCase()
    {
        var offenders = new List<string>();

        var testsRoot = RepoRoot.Combine("tests");
        if (!Directory.Exists(testsRoot))
            Assert.Inconclusive("No tests/ directory in this repo.");

        foreach (var projectDir in Directory.EnumerateDirectories(testsRoot))
        {
            foreach (var sub in Directory.EnumerateDirectories(projectDir))
            {
                var name = Path.GetFileName(sub);
                if (ForbiddenFolders.Contains(name))
                    continue;
                if (SkipFolders.Contains(name))
                    continue;
                if (LowercaseExceptions.Contains(name))
                    continue;
                if (!PascalCaseFolder.IsMatch(name))
                    offenders.Add($"{RepoRoot.Relative(sub)} (folder name not PascalCase)");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Top-level folders inside a test project must be PascalCase (matching the C# "
                    + "namespace convention, e.g. Fixtures/, Fakes/, IntegrationTests/, UnitTests/). "
                    + "snake_case or kebab-case at this level signals a strayed file. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void Repo_Gitignore_Excludes_TestResults()
    {
        var gitignorePath = RepoRoot.Combine(".gitignore");
        File.Exists(gitignorePath).Should().BeTrue($"missing .gitignore at '{gitignorePath}'.");

        var content = File.ReadAllText(gitignorePath);

        // Accept either explicit 'TestResults' or the [Tt]est[Rr]esult glob form
        // (both are recognised by gitignore and produced by `dotnet new gitignore`).
        var hasTestResults =
            Regex.IsMatch(content, @"\bTestResults\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(content, @"\[Tt\]est\[Rr\]esult");

        hasTestResults
            .Should()
            .BeTrue(
                "the repo .gitignore must exclude TestResults/ (dotnet test --collect output) so test "
                    + "run artifacts never get committed. Add a line like '[Tt]est[Rr]esult*/' or "
                    + "'TestResults/'."
            );
    }
}
