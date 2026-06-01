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
    /// Asserts every cross-repo Trax package reference uses the expected floating version, so local
    /// feed builds resolve consistently.
    /// </summary>
    public static GuardResult CrossRepoPackageVersions(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

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

                if (version != options.ExpectedTraxPackageVersion)
                    offenders.Add($"{rel} -> {include} Version=\"{version ?? "<missing>"}\"");
            }
        }

        var message =
            $"Cross-repo Trax package references (Include starts with '{options.TraxPackagePrefix}') must use "
            + $"Version=\"{options.ExpectedTraxPackageVersion}\" so the local feed resolves correctly. "
            + "Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }
}
