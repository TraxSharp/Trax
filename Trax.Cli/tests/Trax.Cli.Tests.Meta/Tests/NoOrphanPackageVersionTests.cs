namespace Trax.Cli.Tests.Meta.Tests;

/// <summary>
/// Every <c>PackageVersion</c> in <c>Directory.Packages.props</c> names a package that some
/// project actually references. Central Package Management lets a pin stand alone, and with
/// <c>CentralPackageTransitivePinningEnabled</c> on that looks like a clean way to force a
/// version onto a package nothing declares.
///
/// <para>
/// Dependabot only updates a <c>PackageVersion</c> it can tie to a <c>PackageReference</c>, so a
/// standalone pin is invisible to it and never moves again. The failure is silent in both
/// directions: a frozen pin becomes NU1109 the moment a direct dependency needs a higher version,
/// which breaks every dependency PR in the repo, and where the pin holds a security floor the
/// floor quietly stops rising. Pin a transitive by adding the reference to the projects that
/// pull it.
/// </para>
///
/// <para>Enforces <c>Trax.Docs/adr/0015-every-packageversion-names-a-referenced-package.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0015-every-packageversion-names-a-referenced-package.md")]
[TestFixture]
public class NoOrphanPackageVersionTests
{
    /// <summary>
    /// Packages allowed to be pinned with nothing referencing them. An entry here is a pin
    /// Dependabot cannot maintain, so it needs a reason that still holds once the pin is frozen.
    /// </summary>
    private static readonly string[] SanctionedStandalonePins = [];

    [Test]
    public void EveryPinnedPackage_IsReferencedBySomeProject()
    {
        var props = RepoRoot.Combine("Directory.Packages.props");
        if (!File.Exists(props))
            Assert.Ignore("this repo manages package versions per project");

        var referenced = ReferencedPackages();

        var orphans = PinnedPackages(props)
            .Where(p => !referenced.Contains(p))
            .Where(p => !SanctionedStandalonePins.Contains(p, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        orphans
            .Should()
            .BeEmpty(
                "a PackageVersion that no project references is invisible to Dependabot and "
                    + "freezes: it becomes a NU1109 downgrade the moment a direct dependency "
                    + "needs more, and a security floor pinned this way stops rising. Add a "
                    + "PackageReference to the projects that pull the package, or delete the "
                    + "pin. Enforces Trax.Docs/adr/0015-every-packageversion-names-a-referenced-package.md. "
                    + "Orphans:\n  "
                    + string.Join("\n  ", orphans)
            );
    }

    [Test]
    public void SanctionedStandalonePins_AreNotStale()
    {
        var props = RepoRoot.Combine("Directory.Packages.props");
        if (!File.Exists(props))
            Assert.Ignore("this repo manages package versions per project");

        var pinned = PinnedPackages(props).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = ReferencedPackages();

        foreach (var name in SanctionedStandalonePins)
        {
            pinned
                .Should()
                .Contain(
                    name,
                    $"'{name}' is exempted from the pin guard but is no longer pinned at all, "
                        + "so the exemption is stale and should be removed."
                );

            referenced
                .Should()
                .NotContain(
                    name,
                    $"'{name}' is exempted from the pin guard but a project now references it, "
                        + "so Dependabot can maintain it and the exemption is stale."
                );
        }
    }

    private static List<string> PinnedPackages(string props) =>
        XDocument
            .Load(props)
            .Descendants("PackageVersion")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Every package named by a <c>PackageReference</c> anywhere in the repo. Directory.Build.props
    /// counts: it is where the repo-wide references live, and a package declared only there is
    /// referenced just as much as one in a csproj.
    /// </summary>
    private static HashSet<string> ReferencedPackages()
    {
        var files = Directory
            .EnumerateFiles(RepoRoot.Path, "*.csproj", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Concat(
                Directory.EnumerateFiles(
                    RepoRoot.Path,
                    "Directory.Build.props",
                    SearchOption.AllDirectories
                )
            );

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            foreach (
                var name in XDocument
                    .Load(file)
                    .Descendants("PackageReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
            )
            {
                referenced.Add(name!);
            }
        }

        return referenced;
    }
}
