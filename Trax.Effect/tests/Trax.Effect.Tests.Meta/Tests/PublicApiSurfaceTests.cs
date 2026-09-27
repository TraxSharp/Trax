using System.Reflection;
using PublicApiGenerator;

namespace Trax.Effect.Tests.Meta.Tests;

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
            typeof(Trax.Effect.Configuration.TraxBuilder.TraxBuilder).Assembly
        ).SetName("Trax.Effect");
        yield return new TestCaseData(typeof(Trax.Effect.Data.AssemblyMarker).Assembly).SetName(
            "Trax.Effect.Data"
        );
        yield return new TestCaseData(
            typeof(Trax.Effect.Data.InMemory.Services.InMemoryContext.InMemoryContext).Assembly
        ).SetName("Trax.Effect.Data.InMemory");
        yield return new TestCaseData(
            typeof(Trax.Effect.Data.Postgres.Services.PostgresContext.PostgresContext).Assembly
        ).SetName("Trax.Effect.Data.Postgres");
        yield return new TestCaseData(
            typeof(Trax.Effect.Data.Sqlite.Services.SqliteContext.SqliteContext).Assembly
        ).SetName("Trax.Effect.Data.Sqlite");
        yield return new TestCaseData(
            typeof(Trax.Effect.Data.Testing.DataLayerGuards).Assembly
        ).SetName("Trax.Effect.Data.Testing");
        yield return new TestCaseData(
            typeof(Trax.Effect.Broadcaster.RabbitMQ.RabbitMqTrainEventBroadcaster).Assembly
        ).SetName("Trax.Effect.Broadcaster.RabbitMQ");
        yield return new TestCaseData(
            typeof(Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkConfiguration).Assembly
        ).SetName("Trax.Effect.Broadcaster.SignalR");
        yield return new TestCaseData(
            typeof(Trax.Effect.JunctionProvider.Logging.Extensions.ServiceExtensions).Assembly
        ).SetName("Trax.Effect.JunctionProvider.Logging");
        yield return new TestCaseData(
            typeof(Trax.Effect.JunctionProvider.Progress.Extensions.ServiceExtensions).Assembly
        ).SetName("Trax.Effect.JunctionProvider.Progress");
        yield return new TestCaseData(
            typeof(Trax.Effect.Provider.Json.Extensions.ServiceExtensions).Assembly
        ).SetName("Trax.Effect.Provider.Json");
        yield return new TestCaseData(
            typeof(Trax.Effect.Provider.Parameter.Configuration.ParameterEffectConfiguration).Assembly
        ).SetName("Trax.Effect.Provider.Parameter");
        yield return new TestCaseData(
            typeof(Trax.Effect.StateMachine.CorpusReplay).Assembly
        ).SetName("Trax.Effect.StateMachine");
        yield return new TestCaseData(
            typeof(Trax.Effect.StateMachine.Persistence.ISnapshotStore).Assembly
        ).SetName("Trax.Effect.StateMachine.Persistence");
        yield return new TestCaseData(
            typeof(Trax.Effect.StateMachine.Testing.DifferentialCorpus).Assembly
        ).SetName("Trax.Effect.StateMachine.Testing");
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
                    + "Adding, removing, or changing a public type/member is a potential breaking change. "
                    + "Trax.Docs/reference/semantic-release.md > Commit Messages: a major version bump on NuGet is permanent."
            );
    }
}
