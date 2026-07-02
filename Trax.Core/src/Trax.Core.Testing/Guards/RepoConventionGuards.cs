using System.Xml.Linq;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Guards;

/// <summary>
/// Repo-structure / packaging guard checkers (read <c>.csproj</c> and <c>Directory.Build.props</c>).
/// </summary>
public static class RepoConventionGuards
{
    /// <summary>
    /// Asserts the root <c>Directory.Build.props</c> pins <c>&lt;Version&gt;</c> to the expected
    /// local-dev value, so locally-packed feed packages always win over nuget.org.
    /// </summary>
    public static GuardResult DirectoryBuildPropsVersion(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var path = Path.Combine(root, "Directory.Build.props");
        var offenders = new List<string>();

        if (!File.Exists(path))
        {
            offenders.Add("Directory.Build.props (missing at repo root)");
        }
        else
        {
            var version = XDocument
                .Load(path)
                .Descendants("Version")
                .FirstOrDefault()
                ?.Value.Trim();

            if (version != options.ExpectedDirectoryBuildPropsVersion)
                offenders.Add(
                    $"Directory.Build.props <Version> is '{version ?? "<missing>"}', expected "
                        + $"'{options.ExpectedDirectoryBuildPropsVersion}'"
                );
        }

        var message =
            $"The root Directory.Build.props <Version> must be '{options.ExpectedDirectoryBuildPropsVersion}' "
            + "for local development; CI overrides it via -p:Version=<semver>. Changing it breaks the "
            + "local-feed-wins-over-nuget.org guarantee. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, 1, message);
    }

    /// <summary>
    /// Asserts every cross-repo Trax package reference is managed by Central Package Management: it
    /// carries no inline <c>Version</c>, and an exact <c>&lt;PackageVersion&gt;</c> pin exists in the
    /// root <c>Directory.Packages.props</c>. Local development overrides those pins to the locally-packed
    /// version via a gitignored <c>trax-local.props</c>; CI restores the exact pins under
    /// <c>--locked-mode</c>.
    /// </summary>
    public static GuardResult CrossRepoPackageVersions(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        // Collect the centrally-pinned package ids from Directory.Packages.props (if present).
        var centralPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cpmPath = Path.Combine(root, "Directory.Packages.props");
        if (File.Exists(cpmPath))
        {
            try
            {
                foreach (var pv in XDocument.Load(cpmPath).Descendants("PackageVersion"))
                {
                    var id = pv.Attribute("Include")?.Value;
                    if (id is not null)
                        centralPins.Add(id);
                }
            }
            catch (Exception ex)
            {
                offenders.Add($"Directory.Packages.props (could not parse: {ex.Message})");
            }
        }

        foreach (var csproj in SourceFiles.ProjectsUnder(root))
        {
            inspected++;
            var rel = Path.GetRelativePath(root, csproj).Replace('\\', '/');
            if (options.CrossRepoPackageKnownExceptions.Contains(rel))
                continue;

            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch (Exception ex)
            {
                offenders.Add($"{rel} (could not parse: {ex.Message})");
                continue;
            }

            foreach (var reference in doc.Descendants("PackageReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (
                    include is null
                    || !include.StartsWith(options.TraxPackagePrefix, StringComparison.Ordinal)
                )
                    continue;

                var version =
                    reference.Attribute("Version")?.Value ?? reference.Element("Version")?.Value;

                if (version is not null)
                    offenders.Add(
                        $"{rel} -> {include} carries inline Version=\"{version}\" (must be centrally managed)"
                    );
                else if (!centralPins.Contains(include))
                    offenders.Add(
                        $"{rel} -> {include} has no <PackageVersion> pin in Directory.Packages.props"
                    );
            }
        }

        var message =
            $"Cross-repo Trax package references (Include starts with '{options.TraxPackagePrefix}') must be managed "
            + "by Central Package Management: no inline Version on the PackageReference, and an exact "
            + "<PackageVersion> pin in Directory.Packages.props (overridden for local dev via trax-local.props). "
            + "Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }
}
