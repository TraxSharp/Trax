namespace Trax.Dashboard.Tests.Meta.Tests;

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
    public void AllCrossRepo_TraxPackageReferences_AreCentrallyManaged()
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
                var version = pkg.Attribute("Version")?.Value ?? pkg.Element("Version")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;
                if (!IsTraxPackage(include))
                    continue;

                // Intra-repo references (a project pointing at a sibling package in the same repo)
                // are rare but legal and are not centrally pinned here; skip them.
                if (
                    thisRepoOwnPrefix is not null
                    && include.StartsWith(thisRepoOwnPrefix, StringComparison.Ordinal)
                )
                    continue;

                // Under Central Package Management the version lives in Directory.Packages.props,
                // so a cross-repo Trax reference must carry no inline Version.
                if (version is not null)
                {
                    offenders.Add(
                        $"{RepoRoot.Relative(csproj)} -> {include} carries inline Version=\"{version}\""
                    );
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "cross-repo Trax package references must be managed by Central Package Management: no "
                    + "inline Version on the PackageReference, with the exact pin in Directory.Packages.props "
                    + "(overridden for local dev to the packed 1.99.99 via trax-local.props). Offenders:\n  "
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

    private static string? DetectThisRepoPrefix()
    {
        var slnx = Directory
            .EnumerateFiles(RepoRoot.Path, "*.slnx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (slnx is null)
            return null;
        var name = Path.GetFileNameWithoutExtension(slnx);
        return TraxPackagePrefixes.Contains(name) ? name : null;
    }
}
