namespace Trax.Core.Tests.Meta.Tests;

[TestFixture]
public class CrossRepoPackageReferenceTests
{
    private static readonly HashSet<string> TraxPackagePrefixes = new(StringComparer.Ordinal)
    {
        "Trax.Core",
        "Trax.Effect",
        "Trax.Mediator",
        "Trax.Scheduler",
        "Trax.Dashboard",
        "Trax.Api",
        "Trax.Cli",
        "Trax.Samples",
    };

    [Test]
    public void AllCrossRepo_TraxPackageReferences_Use_OnePointStar()
    {
        var thisRepoOwnPrefix = DetectThisRepoPrefix();
        var offenders = new List<string>();

        foreach (var csproj in SourceFiles.Projects())
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to parse {RepoRoot.Relative(csproj)}: {ex.Message}");
                return;
            }

            foreach (var pkg in doc.Descendants("PackageReference"))
            {
                var include = pkg.Attribute("Include")?.Value;
                var version = pkg.Attribute("Version")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                if (!IsTraxPackage(include))
                    continue;

                // skip references to packages owned by the current repo itself
                if (
                    thisRepoOwnPrefix is not null
                    && include.StartsWith(thisRepoOwnPrefix, StringComparison.Ordinal)
                )
                    continue;

                if (version != "1.*")
                {
                    offenders.Add(
                        $"{RepoRoot.Relative(csproj)} -> {include} Version=\"{version ?? "<missing>"}\""
                    );
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Local Development Workflow requires all cross-repo Trax PackageReferences "
                    + "to use Version=\"1.*\" so the local .nupkg/ feed (versioned 1.99.99) wins over nuget.org. "
                    + "Pinned or floating-minor versions silently bypass local packages. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static bool IsTraxPackage(string include)
    {
        foreach (var prefix in TraxPackagePrefixes)
        {
            if (include.Equals(prefix, StringComparison.Ordinal))
                return true;
            if (include.StartsWith(prefix + ".", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects the prefix owned by the current repo so we don't flag intra-repo PackageReferences
    /// (which are rare but legal, e.g. an integration test referencing a sibling package).
    /// </summary>
    private static string? DetectThisRepoPrefix()
    {
        var slnx = Directory
            .EnumerateFiles(RepoRoot.Path, "*.slnx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (slnx is null)
            return null;
        var name = Path.GetFileNameWithoutExtension(slnx);
        // .slnx file names are e.g. Trax.Core, Trax.Effect, Trax.Mediator, ...
        return TraxPackagePrefixes.Contains(name) ? name : null;
    }
}
