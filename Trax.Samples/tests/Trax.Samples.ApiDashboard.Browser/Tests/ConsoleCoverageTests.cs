using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// Every route the console serves and every mutation and subscription it sends is named by a browser test's
/// <see cref="CoversAttribute"/>, so a page or a button cannot ship with no test that drives it. The routes are
/// read from <c>ClientApp/src/AppRoutes.tsx</c> (the pages, and the routes from before the navigation moved, which
/// redirect); the operations from <c>ClientApp/console-documents.json</c> (Api ADR 0042) and, for the persisted-
/// operation management the host runs as text, from <c>src/graphql/mutations.ts</c>. It fails naming each route or
/// operation no test covers, and each <c>[Covers]</c> that names one that no longer exists.
/// </summary>
/// <remarks>
/// <para>Not ADR-enforcing: it keeps the console's browser suite as wide as the console, a completeness check on a
/// test suite rather than a decision between alternatives. Browser tests themselves are central ADR 0049.</para>
/// <para>It needs no browser, but runs in this project because the attributes it reads are here.</para>
/// </remarks>
[TestFixture]
public partial class ConsoleCoverageTests
{
    /// <summary>
    /// What no page drives, and why. An entry that names nothing the console has fails the guard like a stale
    /// <c>[Covers]</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<
        string,
        string
    >
    {
        ["route:/dev/mock-overlay"] =
            "the mock's scenario switcher: it exists only in the mock and the demo, never on a host's console",
    };

    [Test]
    public void Every_route_and_mutation_has_a_browser_test()
    {
        var console = Surface();
        var covered = CoveredByTests();

        var uncovered = console
            .Where(c => !covered.ContainsKey(c) && !Excluded.ContainsKey(c))
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = covered
            .Where(c => !console.Contains(c.Key))
            .SelectMany(c => c.Value.Select(test => $"{c.Key} (named by {test})"))
            .Concat(
                Excluded
                    .Keys.Where(e => !console.Contains(e))
                    .Select(e => $"{e} (in the exclusion list)")
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        uncovered
            .Should()
            .BeEmpty(
                "every console route and every mutation and subscription it sends has a browser test that names it "
                    + "with [Covers(...)], or an entry with its reason in ConsoleCoverageTests.Excluded; these have "
                    + "neither"
            );
        stale
            .Should()
            .BeEmpty(
                "every [Covers(...)] names a route in AppRoutes.tsx or an operation in console-documents.json or "
                    + "src/graphql/mutations.ts; these name one that no longer exists"
            );
    }

    /// <summary>The guard reads something on each side, so an empty read cannot pass it.</summary>
    [Test]
    public void The_guard_reads_the_routes_the_operations_and_the_tests()
    {
        var console = Surface();
        console.Should().Contain("route:/runs/:id").And.Contain("route:/executions/:id");
        console.Should().Contain("mutation:RequeueDeadLetter");
        console.Should().Contain("mutation:UploadPersistedOperation");
        console.Should().Contain("subscription:OnJunctionEvent");
        console.Should().NotContain(c => c.StartsWith("query:"));
        CoveredByTests().Should().ContainKey("mutation:RequeueDeadLetter");
    }

    /// <summary>
    /// Everything the console offers: <c>route:</c> each path in AppRoutes.tsx and each moved path,
    /// <c>mutation:</c> and <c>subscription:</c> each operation by its name.
    /// </summary>
    private static HashSet<string> Surface()
    {
        var clientApp = ClientApp();
        var routes = File.ReadAllText(Path.Combine(clientApp, "src", "AppRoutes.tsx"));
        var surface = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in RoutePath().Matches(routes))
            surface.Add($"route:{m.Groups["path"].Value}");
        foreach (Match m in MovedPath().Matches(routes))
            surface.Add($"route:{m.Groups["from"].Value}");

        using var documents = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(clientApp, "console-documents.json"))
        );
        foreach (var document in documents.RootElement.GetProperty("documents").EnumerateArray())
        {
            var m = Operation().Match(document.GetProperty("text").GetString()!);
            if (m.Success && m.Groups["kind"].Value != "query")
                surface.Add($"{m.Groups["kind"].Value}:{m.Groups["name"].Value}");
        }
        // The persisted-operation management documents are left out of console-documents.json: the host runs them
        // as text behind their own gate. The console still sends them from its pages.
        foreach (var file in new[] { "mutations.ts", "subscriptions.ts" })
        foreach (
            Match m in Operation()
                .Matches(File.ReadAllText(Path.Combine(clientApp, "src", "graphql", file)))
        )
            if (m.Groups["kind"].Value != "query")
                surface.Add($"{m.Groups["kind"].Value}:{m.Groups["name"].Value}");

        surface.Count.Should().BeGreaterThan(40, "AppRoutes.tsx and the documents were read");
        return surface;
    }

    /// <summary>Each <c>[Covers]</c> name, with the tests that carry it.</summary>
    private static Dictionary<string, List<string>> CoveredByTests()
    {
        var covered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var methods = typeof(ConsoleCoverageTests)
            .Assembly.GetTypes()
            .SelectMany(t =>
                t.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            );
        foreach (var method in methods)
        foreach (var covers in method.GetCustomAttributes<CoversAttribute>())
        {
            var test = $"{method.DeclaringType!.Name}.{method.Name}";
            if (!covered.TryGetValue(covers.What, out var tests))
                covered[covers.What] = tests = [];
            tests.Add(test);
        }
        return covered;
    }

    private static string ClientApp()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = Path.Combine(
                dir.FullName,
                "Trax.Api",
                "src",
                "Trax.Api.Dashboard",
                "ClientApp"
            );
            if (
                Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "global.json"))
            )
                return candidate;
        }
        throw new InvalidOperationException(
            $"No Trax.Api/src/Trax.Api.Dashboard/ClientApp above {AppContext.BaseDirectory}."
        );
    }

    [GeneratedRegex("<Route\\s+path=\"(?<path>[^\"]+)\"")]
    private static partial Regex RoutePath();

    [GeneratedRegex("\\[\"(?<from>/[^\"]*)\",\\s*\"/[^\"]*\"\\]")]
    private static partial Regex MovedPath();

    [GeneratedRegex(@"\b(?<kind>query|mutation|subscription)\s+(?<name>[A-Z]\w*)")]
    private static partial Regex Operation();
}
