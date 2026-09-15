namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// A sample E2E factory that boots its host against PostgreSQL names a port and a database CI
/// provisions up front: a fixed service port plus an explicit create-databases list. A factory that
/// points anywhere CI does not provision either fails to connect or, worse, silently skips, reporting
/// a green build while testing nothing (which is exactly how the Bookworm suite once hid a
/// 0%-coverage gap behind a local-only port 5433). This pins every Postgres factory's default
/// connection string to the CI contract so the two cannot drift apart unnoticed. Samples on SQLite
/// or the in-memory provider declare no connection string, so there is nothing here to check.
///
/// <para>Enforces <c>docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md")]
[TestFixture]
public class E2EDatabaseProvisioningTests
{
    private static readonly Regex ConnectionString = new(
        @"Port=(?<port>\d+);Database=(?<db>[^;]+)",
        RegexOptions.Compiled
    );

    [Test]
    public void EveryE2EFactory_TargetsACiProvisionedDatabase()
    {
        var workflow = File.ReadAllText(
            RepoRoot.Combine(".github", "workflows", "pull_request.yml")
        );

        // Host ports the workflow maps onto the Postgres service container (e.g. "5432:5432").
        var hostPorts = Regex
            .Matches(workflow, @"(?<host>\d+):5432")
            .Select(m => m.Groups["host"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Databases the run can reach: the service's POSTGRES_DB plus everything the setup step
        // creates in its `for db in ...; do` loop.
        var provisioned = new HashSet<string>(StringComparer.Ordinal);
        var defaultDb = Regex.Match(workflow, @"POSTGRES_DB:\s*(?<db>\S+)");
        if (defaultDb.Success)
            provisioned.Add(defaultDb.Groups["db"].Value);
        var createLoop = Regex.Match(workflow, @"for db in (?<dbs>[^;]+);");
        if (createLoop.Success)
            foreach (
                var db in createLoop
                    .Groups["dbs"]
                    .Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            )
                provisioned.Add(db);

        hostPorts.Should().NotBeEmpty("the workflow must map at least one Postgres host port");
        provisioned.Should().NotBeEmpty("the workflow must provision at least one database");

        var offenders = new List<string>();
        var inspected = 0;

        // Most suites keep their factory in a Factories/ folder; PersistedOperations keeps its in
        // Fixtures/. Match on either, because a factory this scan misses is a suite the guard
        // cannot vouch for while still reporting green.
        var factories = SourceFiles
            .CSharp("tests")
            .Where(f =>
                f.Contains(
                    $"{Path.DirectorySeparatorChar}Factories{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                ) || Path.GetFileName(f).EndsWith("Factory.cs", StringComparison.Ordinal)
            );

        foreach (var file in factories)
        foreach (Match match in ConnectionString.Matches(File.ReadAllText(file)))
        {
            inspected++;
            var port = match.Groups["port"].Value;
            var db = match.Groups["db"].Value;
            var rel = RepoRoot.Relative(file);

            if (!hostPorts.Contains(port))
                offenders.Add(
                    $"{rel}: connection port {port} is not mapped to the CI Postgres service "
                        + $"(mapped host ports: {string.Join(", ", hostPorts.OrderBy(p => p))})."
                );
            else if (!provisioned.Contains(db))
                offenders.Add(
                    $"{rel}: database '{db}' is not provisioned in CI. Add it to the "
                        + "create-databases loop in .github/workflows (provisioned: "
                        + $"{string.Join(", ", provisioned.OrderBy(d => d))})."
                );
        }

        inspected
            .Should()
            .BeGreaterThan(
                4,
                "the five Postgres factory connection strings in this repo must all be inspected. "
                    + "Fewer means either a suite was deleted, in which case lower this floor "
                    + "deliberately, or the connection-string shape changed and this guard is "
                    + "silently passing"
            );
        offenders
            .Should()
            .BeEmpty(
                "every E2E factory must target the CI Postgres port and a provisioned database. A "
                    + "factory pointing elsewhere skips and reports green while testing nothing. See "
                    + "docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md:\n"
                    + string.Join("\n", offenders)
            );
    }
}
