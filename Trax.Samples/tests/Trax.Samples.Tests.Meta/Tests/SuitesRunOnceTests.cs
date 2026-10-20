using System.Text.Json.Nodes;

namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// A test project of this folder that CI runs in a job of its own (a suite in
/// <c>.github/ci/packages.json</c>) is left out of this folder's job, so its tests run once. The
/// folder's job tests the whole solution; without the filter in its row's <c>test-args</c> it
/// runs the suite's tests too, behind every other project, which is what the suite's job exists
/// to avoid. Each suite also names a project that exists.
///
/// <para>Not ADR-enforcing: it keeps the split of the CI jobs consistent, a property of how the
/// jobs are configured rather than a choice between alternatives.</para>
/// </summary>
[TestFixture]
public class SuitesRunOnceTests
{
    [Test]
    public void Every_suite_of_this_folder_is_left_out_of_its_job()
    {
        var config = JsonNode.Parse(
            File.ReadAllText(MonorepoRoot.Combine(".github", "ci", "packages.json"))
        )!;
        var testArgs =
            (string?)
                config["packages"]!.AsArray().Single(p => (string?)p!["folder"] == "Trax.Samples")![
                    "dotnet"
                ]!["test-args"]
            ?? "";
        var suites = (config["suites"]?.AsArray() ?? [])
            .Where(s => (string?)s!["folder"] == "Trax.Samples")
            .Select(s => (string)s!["project"]!)
            .ToList();

        suites.Should().NotBeEmpty("the console's browser tests run in a job of their own");

        var problems = new List<string>();
        foreach (var project in suites)
        {
            // A solution of its own is not in this folder's solution, so the folder's job never
            // runs it; it only has to exist and share no project with the folder's solution.
            if (project.EndsWith(".slnx", StringComparison.Ordinal))
            {
                if (!File.Exists(RepoRoot.Combine(project)))
                    problems.Add($"{project}: no such solution.");
                else if (SharedProjects(project) is { Count: > 0 } shared)
                    problems.Add(
                        $"{project}: shares {string.Join(", ", shared)} with Trax.Samples.slnx, "
                            + "whose job would run its tests too."
                    );
                continue;
            }
            var name = Path.GetFileName(project);
            if (!File.Exists(RepoRoot.Combine(project, name + ".csproj")))
                problems.Add($"{project}: no {name}.csproj there.");
            if (!testArgs.Contains($"FullyQualifiedName!~{name}.", StringComparison.Ordinal))
                problems.Add(
                    $"{project}: the Trax.Samples row's test-args does not leave it out "
                        + $"(add FullyQualifiedName!~{name}. to its --filter), so its tests run twice."
                );
        }

        problems.Should().BeEmpty(string.Join("\n", problems));
    }

    private static List<string> SharedProjects(string solution)
    {
        var dir = Path.GetDirectoryName(RepoRoot.Combine(solution))!;
        var own = ProjectsIn(RepoRoot.Combine(solution), dir);
        var folders = ProjectsIn(RepoRoot.Combine("Trax.Samples.slnx"), RepoRoot.Path);
        return own.Intersect(folders).Select(RepoRoot.Relative).ToList();
    }

    private static HashSet<string> ProjectsIn(string solution, string dir) =>
        System
            .Xml.Linq.XDocument.Load(solution)
            .Descendants("Project")
            .Select(p => Path.GetFullPath(Path.Combine(dir, (string)p.Attribute("Path")!)))
            .ToHashSet(StringComparer.Ordinal);
}
