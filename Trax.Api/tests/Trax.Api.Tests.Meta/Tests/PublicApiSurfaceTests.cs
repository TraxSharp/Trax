using System.Reflection;
using PublicApiGenerator;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// The published surface is a committed file, so a change to it lands in the diff.
///
/// <para>Enforces <c>Trax.Docs/adr/0010-the-public-api-surface-is-a-committed-baseline.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0010-the-public-api-surface-is-a-committed-baseline.md")]
[TestFixture]
public class PublicApiSurfaceTests
{
    private static readonly string BaselineDir = Path.Combine(
        AppContext.BaseDirectory,
        "PublicApi"
    );

    private static readonly string BaselineSourceDir = Path.Combine(
        Path.GetDirectoryName(typeof(PublicApiSurfaceTests).Assembly.Location)!,
        "..",
        "..",
        "..",
        "PublicApi"
    );

    public static IEnumerable<TestCaseData> Assemblies()
    {
        yield return new TestCaseData(
            typeof(Trax.Api.Exceptions.TrainAuthorizationException).Assembly
        ).SetName("Trax.Api");
        yield return new TestCaseData(typeof(Trax.Api.Auth.TraxPrincipal).Assembly).SetName(
            "Trax.Api.Auth"
        );
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions).Assembly
        ).SetName("Trax.Api.GraphQL");
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.PersistedOperations.IPersistedOperationsCapability).Assembly
        ).SetName("Trax.Api.GraphQL.PersistedOperations");
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Client.GraphQLClientConfigurationBuilder).Assembly
        ).SetName("Trax.Api.GraphQL.Client");
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Audit.TraxAuditOptions).Assembly
        ).SetName("Trax.Api.GraphQL.Audit");
        yield return new TestCaseData(typeof(Trax.Api.Auth.ApiKey.ApiKeyBuilder).Assembly).SetName(
            "Trax.Api.Auth.ApiKey"
        );
        yield return new TestCaseData(typeof(Trax.Api.Auth.Jwt.JwtBuilder).Assembly).SetName(
            "Trax.Api.Auth.Jwt"
        );
        yield return new TestCaseData(
            typeof(Trax.Api.Auth.Jwt.Cognito.CognitoDefaults).Assembly
        ).SetName("Trax.Api.Auth.Jwt.Cognito");
        yield return new TestCaseData(
            typeof(Trax.Api.Auth.Jwt.Cognito.Issuer.CognitoTokenIssuer).Assembly
        ).SetName("Trax.Api.Auth.Jwt.Cognito.Issuer");
        yield return new TestCaseData(
            typeof(Trax.Api.Auth.Jwt.Testing.TestTokenIssuer).Assembly
        ).SetName("Trax.Api.Auth.Jwt.Testing");
        yield return new TestCaseData(typeof(Trax.Api.Auth.Oidc.OidcDefaults).Assembly).SetName(
            "Trax.Api.Auth.Oidc"
        );
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Client.Trax.AssemblySchemaProvider).Assembly
        ).SetName("Trax.Api.GraphQL.Client.Trax");
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Client.Typed.GraphQLOperationAttribute).Assembly
        ).SetName("Trax.Api.GraphQL.Client.Typed");
        yield return new TestCaseData(
            typeof(Trax.Api.GraphQL.Testing.CrossSchemaGuards).Assembly
        ).SetName("Trax.Api.GraphQL.Testing");
    }

    /// <summary>
    /// Every package this repo publishes has a baseline, so a new one cannot ship unguarded.
    /// </summary>
    /// <remarks>
    /// The list above is hand-written, which is how Trax.Effect.Provider.Parameter went five
    /// releases without one: the assembly was referenced by this project and simply never added.
    /// Reading the projects off disk is the only version of this check that notices a package
    /// nobody remembered.
    /// </remarks>
    [Test]
    public void EveryPublishedProject_HasABaseline()
    {
        var srcDir = RepoRoot.Combine("src");
        Directory.Exists(srcDir).Should().BeTrue("missing 'src'.");

        var missing = new List<string>();

        foreach (var projectDir in Directory.EnumerateDirectories(srcDir))
        {
            var name = Path.GetFileName(projectDir);
            var csproj = Path.Combine(projectDir, $"{name}.csproj");
            if (!File.Exists(csproj))
                continue;

            // A project that opts out of packing publishes nothing, so it has no surface to pin.
            if (
                File.ReadAllText(csproj)
                    .Contains("<IsPackable>false</IsPackable>", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            if (!File.Exists(Path.Combine(BaselineSourceDir, $"{name}.received.txt")))
                missing.Add(name);
        }

        missing
            .Should()
            .BeEmpty(
                "every project under src/ that packs must have a committed public API baseline, "
                    + "and be listed in Assemblies() above so PublicApi_Matches_CheckedInBaseline "
                    + "compares it. Trax.Docs/adr/0010-the-public-api-surface-is-a-committed-baseline.md "
                    + "is the rule. Without one, a breaking change to that package reaches NuGet "
                    + "without appearing in any diff. Missing:\n  "
                    + string.Join("\n  ", missing)
            );
    }

    [TestCaseSource(nameof(Assemblies))]
    public void PublicApi_Matches_CheckedInBaseline(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        var current = assembly.GeneratePublicApi(
            new ApiGeneratorOptions { IncludeAssemblyAttributes = false }
        );

        var baselinePath = Path.Combine(BaselineDir, $"{name}.received.txt");

        if (!File.Exists(baselinePath))
        {
            Directory.CreateDirectory(BaselineDir);
            File.WriteAllText(baselinePath, current);
            try
            {
                Directory.CreateDirectory(BaselineSourceDir);
                File.WriteAllText(Path.Combine(BaselineSourceDir, $"{name}.received.txt"), current);
            }
            catch
            {
                // best-effort write to source tree
            }
            Assert.Fail(
                $"No public API baseline for '{name}'. A baseline has been written to "
                    + $"'PublicApi/{name}.received.txt' in the test source tree. Review, commit, re-run."
            );
            return;
        }

        var baseline = File.ReadAllText(baselinePath);

        string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";

        Normalize(current)
            .Should()
            .Be(
                Normalize(baseline),
                $"public API of '{name}' must match the checked-in baseline at "
                    + $"PublicApi/{name}.received.txt. If this change is intentional, update the baseline. "
                    + "Trax.Docs/contributing/releases.md > Commit types: a major version bump on NuGet is permanent."
            );
    }
}
