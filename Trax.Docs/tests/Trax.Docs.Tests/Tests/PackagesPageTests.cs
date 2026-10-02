namespace Trax.Docs.Tests.Tests;

/// <summary>
/// The packages page lists every package Trax publishes, each linked to nuget.org.
///
/// <para>The code repos are not checked out beside this one in CI, so the test cannot discover the
/// packable projects itself. It owns the list instead. When a repo adds, renames or retires a
/// packable <c>Trax.*</c> project, update <see cref="PublishedPackages"/> and the page together.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
public class PackagesPageTests
{
    private const string Adr = "Trax.Docs/adr/0008-documentation-conventions-are-linted.md";

    /// <summary>
    /// Every packable <c>Trax.*</c> project on the eight code repos' <c>main</c>, as of 2026-10-01.
    /// </summary>
    private static readonly string[] PublishedPackages =
    [
        // Trax.Core
        "Trax.Core",
        "Trax.Core.Analyzers",
        "Trax.Core.Testing",
        // Trax.Effect
        "Trax.Effect",
        "Trax.Effect.Broadcaster.RabbitMQ",
        "Trax.Effect.Broadcaster.SignalR",
        "Trax.Effect.Data",
        "Trax.Effect.Data.InMemory",
        "Trax.Effect.Data.Postgres",
        "Trax.Effect.Data.Sqlite",
        "Trax.Effect.Data.Testing",
        "Trax.Effect.Decisions.SystemOne",
        "Trax.Effect.JunctionProvider.Logging",
        "Trax.Effect.JunctionProvider.Progress",
        "Trax.Effect.Provider.Json",
        "Trax.Effect.Provider.Parameter",
        "Trax.Effect.StateMachine",
        "Trax.Effect.StateMachine.Persistence",
        "Trax.Effect.StateMachine.Testing",
        // Trax.Mediator
        "Trax.Mediator",
        "Trax.Mediator.Testing",
        // Trax.Scheduler
        "Trax.Runner.Lambda",
        "Trax.Scheduler",
        "Trax.Scheduler.Lambda",
        "Trax.Scheduler.Sqs",
        // Trax.Api
        "Trax.Api",
        "Trax.Api.Auth",
        "Trax.Api.Auth.ApiKey",
        "Trax.Api.Auth.Jwt",
        "Trax.Api.Auth.Jwt.Cognito",
        "Trax.Api.Auth.Jwt.Cognito.Issuer",
        "Trax.Api.Auth.Jwt.Testing",
        "Trax.Api.Auth.Oidc",
        "Trax.Api.GraphQL",
        "Trax.Api.GraphQL.Audit",
        "Trax.Api.GraphQL.Client",
        "Trax.Api.GraphQL.Client.Trax",
        "Trax.Api.GraphQL.Client.Typed",
        "Trax.Api.GraphQL.PersistedOperations",
        "Trax.Api.GraphQL.Testing",
        // Trax.Dashboard
        "Trax.Dashboard",
        // Trax.Cli
        "Trax.Cli",
        // Trax.Samples
        "Trax.Samples.Templates",
    ];

    private static readonly Regex NuGetLink = new(
        @"\]\(https://www\.nuget\.org/packages/(?<id>Trax\.[A-Za-z0-9.]+)\)",
        RegexOptions.Compiled
    );

    private static HashSet<string> LinkedPackages()
    {
        var page = File.ReadAllText(RepoRoot.Combine("reference", "packages.md"));
        return NuGetLink
            .Matches(page)
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Test]
    public void PackagesPage_Links_EveryPublishedPackage()
    {
        var linked = LinkedPackages();

        PublishedPackages
            .Where(id => !linked.Contains(id))
            .Should()
            .BeEmpty(
                "reference/packages.md must list every published Trax package with a "
                    + "[Name](https://www.nuget.org/packages/Name) link. Add a row for each missing one. See "
                    + Adr
                    + "."
            );
    }

    [Test]
    public void PackagesPage_Links_OnlyKnownPackages()
    {
        var known = PublishedPackages.ToHashSet(StringComparer.Ordinal);

        LinkedPackages()
            .Where(id => !known.Contains(id))
            .Should()
            .BeEmpty(
                "every package reference/packages.md links must be in PublishedPackages, so the "
                    + "list stays the record of what ships. Add it there, or remove the row for a "
                    + "package that no longer ships. See "
                    + Adr
                    + "."
            );
    }
}
