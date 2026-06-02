namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every sample E2E factory boots its host against a PostgreSQL database, and CI provisions those
/// databases up front: a fixed service port plus an explicit create-databases list. A factory that
/// points anywhere CI does not provision either fails to connect or, worse, silently skips, reporting
/// a green build while testing nothing (which is exactly how the Bookworm suite once hid a
/// 0%-coverage gap behind a local-only port 5433). This pins every factory's default connection
/// string to the CI contract so the two cannot drift apart unnoticed.
/// </summary>
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

        var factories = SourceFiles
            .CSharp("tests")
            .Where(f =>
                f.Contains(
                    $"{Path.DirectorySeparatorChar}Factories{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
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
                2,
                "the scan should find the sample E2E factory connection strings; finding none means "
                    + "the factory connection-string shape changed and this guard is silently passing"
            );
        offenders
            .Should()
            .BeEmpty(
                "every E2E factory must target the CI Postgres port and a provisioned database:\n"
                    + string.Join("\n", offenders)
            );
    }
}
