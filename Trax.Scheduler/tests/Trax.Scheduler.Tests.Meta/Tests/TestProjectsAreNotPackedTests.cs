namespace Trax.Scheduler.Tests.Meta.Tests;

/// <summary>
/// Nothing under <c>tests/</c> is packed: every project there sets <c>IsPackable</c> to false in
/// its own project file.
///
/// <para>A test project is not packable by default, because the test SDK says so, but a helper
/// library beside the tests is an ordinary class library and packs like one.
/// <c>Trax.Scheduler.Tests.ArrayLogger</c> is such a library, and it once reached nuget.org as a
/// package of its own. The release workflow packs the whole solution, so the project file is the
/// only thing between a new helper library and the feed.</para>
///
/// <para>Not ADR-enforcing: it pins a packaging fact (the published packages are the libraries
/// under <c>src/</c>), which no decision between designs produced.</para>
/// </summary>
[TestFixture]
public class TestProjectsAreNotPackedTests
{
    public static IEnumerable<TestCaseData> TestProjects()
    {
        var testsRoot = RepoRoot.Combine("tests");
        if (!Directory.Exists(testsRoot))
            yield break;

        foreach (
            var project in Directory
                .EnumerateFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(p =>
                    !p.Split(System.IO.Path.DirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj")
                )
                .Order(StringComparer.Ordinal)
        )
            yield return new TestCaseData(project).SetName(
                $"NotPacked({System.IO.Path.GetFileNameWithoutExtension(project)})"
            );
    }

    [TestCaseSource(nameof(TestProjects))]
    public void TestProject_SetsIsPackableFalse(string projectPath)
    {
        var isPackable = XDocument
            .Load(projectPath)
            .Descendants("IsPackable")
            .Where(e =>
                e.Attribute("Condition") is null && e.Parent?.Attribute("Condition") is null
            )
            .Select(e => e.Value.Trim())
            .LastOrDefault();

        isPackable
            .Should()
            .Be(
                "false",
                $"'{RepoRoot.Relative(projectPath)}' is under tests/ and must not be packed: set "
                    + "<IsPackable>false</IsPackable> in it. The release workflow packs every "
                    + "project in the solution, and a helper library beside the tests packs like "
                    + "any class library (Trax.Scheduler.Tests.ArrayLogger once reached nuget.org "
                    + "that way)."
            );
    }

    [Test]
    public void TheArrayLoggerHelperLibrary_IsCovered()
    {
        TestProjects()
            .Select(c => (string)c.Arguments[0]!)
            .Should()
            .Contain(
                p =>
                    p.EndsWith("Trax.Scheduler.Tests.ArrayLogger.csproj", StringComparison.Ordinal),
                "the helper library that once shipped by accident must stay under this guard"
            );
    }
}
