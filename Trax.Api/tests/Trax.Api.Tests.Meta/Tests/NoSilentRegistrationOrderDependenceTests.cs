using System.Text.RegularExpressions;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// Reading the <c>IServiceCollection</c> inside a registration extension asks "is this
/// registered <i>yet</i>", not "will this be registered". The answer depends on where the
/// caller happens to be in their own startup code, so any behaviour derived from it changes
/// with registration order.
/// </summary>
/// <remarks>
/// Two real bugs came from this. <c>AddTraxGraphQL()</c> skipped bridging
/// <c>IAuthenticationSchemeProvider</c> for hosts that call <c>AddAuthentication()</c>
/// afterwards, so every request 500'd. Worse, it skipped the subscription interceptor for a
/// scheme registered afterwards, and HotChocolate's default accepted every
/// <c>connection_init</c> — subscriptions stopped authenticating while HTTP kept working.
/// <para>
/// The rule is not "never inspect the collection". Idempotency guards and preconditions are
/// fine, because they cannot silently change behaviour. The rule is that nothing may be
/// <i>silently</i> different because of order: either defer the decision to a point where the
/// container is complete, or assert the ordering at startup and throw. See
/// <c>Trax.Docs/reference/registration-order.md</c>.
/// </para>
/// </remarks>
[TestFixture]
public class NoSilentRegistrationOrderDependenceTests
{
    private static readonly Regex CollectionIntrospection = new(
        @"\b[Ss]ervices\.(Any|All|First|FirstOrDefault|Last|LastOrDefault|Single|SingleOrDefault|Where|Select|Count)\s*\(",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Every site that reads the collection today, with the count it reads it at. Each is
    /// either an idempotency guard, a precondition that throws, or a decision backed by a
    /// startup validator that fails loudly when the ordering was wrong.
    /// </summary>
    /// <remarks>
    /// To add an entry you must first make the site safe: pair it with a validator that throws
    /// at host start, or restructure so the decision happens once the container is built. Do
    /// not raise a count to silence this test.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> ReviewedSites = new Dictionary<
        string,
        int
    >(StringComparer.Ordinal)
    {
        // Idempotency guard for the disclaimer hosted service.
        ["src/Trax.Api.Auth.ApiKey/ApiKeyAuthServiceCollectionExtensions.cs"] = 1,
        // Scheme dedupe + disclaimer idempotency guard.
        ["src/Trax.Api.Auth.Jwt/JwtAuthServiceCollectionExtensions.cs"] = 2,
        // Get-or-create accumulator shared across repeated AddTraxJwtAuth calls.
        ["src/Trax.Api.Auth.Jwt/JwtResolverRegistry.cs"] = 1,
        // Idempotency guard for the disclaimer hosted service.
        ["src/Trax.Api.Auth.Oidc/OidcAuthServiceCollectionExtensions.cs"] = 1,
        // Idempotency guard for the disclaimer hosted service.
        ["src/Trax.Api.GraphQL.Audit/TraxGraphQLBuilderAuditExtensions.cs"] = 1,
        // AddTrax precondition (throws), the broadcaster branch (its trigger is registered
        // inside AddTrax, which the precondition already forces to come first), the train
        // discovery snapshot, and the three subscription-interceptor branches, which
        // TraxSubscriptionAuthWiringValidator asserts at startup.
        ["src/Trax.Api.GraphQL/Extensions/GraphQLServiceExtensions.cs"] = 6,
    };

    [Test]
    public void ProductionSources_DoNotIntroduce_NewRegistrationOrderDependence()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RepoRelative(file);
            var count = CollectionIntrospection.Matches(File.ReadAllText(file)).Count;

            if (count == 0)
                continue;

            if (!ReviewedSites.TryGetValue(relative, out var reviewed))
            {
                offenders.Add($"{relative}: {count} new (file is not in the reviewed list)");
                continue;
            }

            if (count > reviewed)
                offenders.Add($"{relative}: {count} sites, {reviewed} reviewed");
        }

        offenders
            .Should()
            .BeEmpty(
                "reading the IServiceCollection during registration makes behaviour depend on "
                    + "where the caller is in their startup code. Either defer the decision until "
                    + "the container is complete, or add a startup validator that throws when the "
                    + "ordering was wrong, then add the site to ReviewedSites with a reason. See "
                    + "Trax.Docs/reference/registration-order.md.\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void ReviewedSites_AreAllStillPresent()
    {
        // A stale entry hides the fact that a site was cleaned up, and lets a new one take its
        // place silently.
        var missing = ReviewedSites
            .Keys.Where(relative => !File.Exists(Path.Combine(RepoRoot(), relative)))
            .ToList();

        missing.Should().BeEmpty("ReviewedSites should not name files that no longer exist");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            );

    private static string RepoRelative(string file) =>
        Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Api.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root.");
    }
}
