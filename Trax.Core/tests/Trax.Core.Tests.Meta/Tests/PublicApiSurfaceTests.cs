using System.Reflection;
using PublicApiGenerator;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// The published surface of <c>Trax.Core</c> and <c>Trax.Core.Testing</c> is a committed file,
/// so a change to it lands in the diff. <c>Trax.Core.Analyzers</c> ships as a development
/// dependency that loads into the compiler, so it has no baseline.
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
        // Reference a single type from each in-scope assembly so it gets loaded.
        yield return new TestCaseData(typeof(Trax.Core.Exceptions.TrainException).Assembly).SetName(
            "Trax.Core"
        );
        yield return new TestCaseData(typeof(Trax.Core.Testing.GuardResult).Assembly).SetName(
            "Trax.Core.Testing"
        );
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

            // An analyzer loads into the compiler; it has no API a consumer calls.
            if (name == "Trax.Core.Analyzers")
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
            // First run: write a baseline to the test bin/ output and to the source tree
            // so the developer can commit it. Fail with a clear message.
            Directory.CreateDirectory(BaselineDir);
            File.WriteAllText(baselinePath, current);
            try
            {
                Directory.CreateDirectory(BaselineSourceDir);
                File.WriteAllText(Path.Combine(BaselineSourceDir, $"{name}.received.txt"), current);
            }
            catch
            {
                // best-effort write to source tree; fine if the working directory is read-only
            }
            Assert.Fail(
                $"No public API baseline for '{name}'. A baseline has been written to "
                    + $"'PublicApi/{name}.received.txt' in the test source tree. Review it, commit it, "
                    + "and re-run."
            );
            return;
        }

        var baseline = File.ReadAllText(baselinePath);

        // Normalise line endings + trailing whitespace for cross-platform stability.
        string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";

        Normalize(current)
            .Should()
            .Be(
                Normalize(baseline),
                $"public API of '{name}' must match the checked-in baseline at "
                    + $"PublicApi/{name}.received.txt. If this change is intentional, update the baseline. "
                    + "Adding, removing, or changing a public type/member is a potential breaking change — "
                    + "the snapshot makes it deliberate. (Trax.Docs/contributing/releases.md > Commit types: a major version "
                    + "bump on NuGet is permanent.)"
            );
    }
}
