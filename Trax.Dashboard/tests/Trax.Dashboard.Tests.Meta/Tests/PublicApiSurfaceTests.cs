using System.Reflection;
using PublicApiGenerator;

namespace Trax.Dashboard.Tests.Meta.Tests;

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
            typeof(Trax.Dashboard.Configuration.DashboardOptions).Assembly
        ).SetName("Trax.Dashboard");
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
                    + "CLAUDE.md > Versioning Strategy: a major version bump on NuGet is permanent."
            );
    }
}
