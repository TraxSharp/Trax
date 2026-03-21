using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Trax.Samples.TestRunner.Models;

namespace Trax.Samples.TestRunner.Services;

public class TestProjectRegistry
{
    private static readonly HashSet<string> ExcludedSuffixes = ["ArrayLogger", "Benchmarks"];

    private readonly Lazy<IReadOnlyList<TestProject>> _projects;

    public TestProjectRegistry(IConfiguration configuration, ILogger<TestProjectRegistry> logger)
    {
        var root =
            configuration["TestRunner:Root"]
            ?? throw new InvalidOperationException("TestRunner:Root configuration is required.");

        var resolvedRoot = Path.GetFullPath(root);
        logger.LogInformation("TestProjectRegistry scanning {Root}", resolvedRoot);

        _projects = new Lazy<IReadOnlyList<TestProject>>(() => ScanProjects(resolvedRoot, logger));
    }

    public IReadOnlyList<TestProject> Projects => _projects.Value;

    private static List<TestProject> ScanProjects(string root, ILogger logger)
    {
        var projects = new List<TestProject>();
        var testsDir = Path.Combine(root, "tests");

        if (!Directory.Exists(testsDir))
        {
            logger.LogWarning("No tests/ directory found at {Root}", root);
            return projects;
        }

        foreach (var projectDir in Directory.GetDirectories(testsDir))
        {
            var projectName = Path.GetFileName(projectDir);

            if (ExcludedSuffixes.Any(suffix => projectName.EndsWith(suffix)))
                continue;

            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            if (!File.Exists(csprojPath))
                continue;

            if (!IsNUnitProject(csprojPath))
                continue;

            projects.Add(new TestProject { Name = projectName, ProjectPath = csprojPath });

            logger.LogDebug("Discovered test project {Name}", projectName);
        }

        logger.LogInformation("Discovered {Count} test projects", projects.Count);
        return projects.OrderBy(p => p.Name).ToList();
    }

    private static bool IsNUnitProject(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants("PackageReference")
                .Any(e =>
                    string.Equals(
                        e.Attribute("Include")?.Value,
                        "NUnit",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }
        catch
        {
            return false;
        }
    }
}
