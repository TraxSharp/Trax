using System.Reflection;
using PublicApiGenerator;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// The published surface of <c>Trax.Core</c> is a committed file, so a change to it lands in
/// the diff. It is the only baseline in this repo. <c>Trax.Core.Testing</c> also publishes, and
/// consumers bind to it by subclassing <c>HygieneGuardFixture</c> and reading
/// <c>ArchitectureGuardOptions</c>, so its surface is unpinned today and a break there would not
/// show up in a diff. <c>Trax.Core.Analyzers</c> ships as a development dependency.
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
        // Trax.Core only: see the summary for the two published assemblies with no baseline.
        yield return new TestCaseData(typeof(Trax.Core.Exceptions.TrainException).Assembly).SetName(
            "Trax.Core"
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
                    + "the snapshot makes it deliberate. (Trax.Docs/reference/semantic-release.md > Commit Messages: a major version "
                    + "bump on NuGet is permanent.)"
            );
    }
}
