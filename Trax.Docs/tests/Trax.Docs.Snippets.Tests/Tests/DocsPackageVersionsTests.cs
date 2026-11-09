using System.Diagnostics;

namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every Trax package version written on a page is one exact version, the same for every package,
/// and every package a page pins shipped at that version.
///
/// <para>Every Trax package releases at one version
/// (<c>Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md</c>), and the
/// site is published from a release, so a page that tells a reader to install two different
/// versions names at least one that was never released alongside the code the snippets were
/// compiled against. A floating <c>Version="1.*"</c> is refused too: it restores whatever was
/// published last, which a later release has already broken once.</para>
///
/// <para>The production site stamps the latest release into these pins
/// (<c>Trax.Website/scripts/sync-docs.sh</c>), but a page read anywhere else (on GitHub, in a
/// preview) shows the version written here. A package that first shipped after that version fails
/// the reader's restore with NU1102, so each pinned package must be a packable project in the tree
/// of the tag <c>v&lt;version&gt;</c>. A package no release has shipped yet (documented in the same
/// change as its code) is allowed until the next release ships it.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class DocsPackageVersionsTests
{
    private static readonly Regex TraxReference = new(
        @"<Package(?:Reference|Version)\s+Include=""(?<id>Trax\.[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.Compiled
    );

    /// <summary><c>dotnet tool install Trax.Cli --version x.y.z</c>, <c>dotnet add package Trax.X --version x.y.z</c>.</summary>
    private static readonly Regex TraxCommand = new(
        @"dotnet\s+(?:tool\s+install|add\s+package)\s+(?:--global\s+)?(?<id>Trax\.\S+)\s+--version\s+(?<version>\S+)",
        RegexOptions.Compiled
    );

    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    private static readonly Regex ReleaseTag = new(
        @"^v(?<version>\d+\.\d+\.\d+)$",
        RegexOptions.Compiled
    );

    /// <summary>A package's project: <c>Trax.X/src/Id/Id.csproj</c> or <c>Trax.X/dotnet/src/Id/Id.csproj</c>.</summary>
    private static readonly Regex PackageProject = new(
        @"^Trax\.[^/]+/(?:dotnet/)?src/(?<id>[^/]+)/\k<id>\.csproj$",
        RegexOptions.Compiled
    );

    [Test]
    public void TraxVersionsOnPages_AreOneExactVersion()
    {
        var found = Pins();

        var versions = found.Select(f => f.Version).Distinct(StringComparer.Ordinal).ToList();
        var offenders = found
            .Where(f => !ExactVersion.IsMatch(f.Version) || versions.Count > 1)
            .Select(f => $"{f.Where}  {f.Id} {f.Version}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "every Trax package releases at one version, so a page names one exact version for "
                    + $"all of them (found: {string.Join(", ", versions)}). See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md and "
                    + "Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void TraxPackagesOnPages_ShippedAtTheVersionPinned()
    {
        var found = Pins().Where(f => ExactVersion.IsMatch(f.Version)).ToList();
        if (found.Count == 0)
            return;

        var releases = Git("tag", "--list", "v*")
            .Select(t => ReleaseTag.Match(t.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups["version"].Value)
            .ToHashSet(StringComparer.Ordinal);
        releases
            .Should()
            .NotBeEmpty(
                "this test reads the release tags. A shallow clone has none: fetch the pinned and "
                    + "the newest release tag, as CI does (.github/scripts/fetch-docs-release-tags.sh)"
            );

        var latest = releases.MaxBy(Version.Parse)!;
        var packagesAtLatest = PackagesAt($"v{latest}");
        var packagesAtHead = PackagesAt("HEAD");
        var packagesAt = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var (where, id, version) in found)
        {
            if (!releases.Contains(version))
            {
                offenders.Add(
                    $"{where}  {id} {version}: no tag v{version}; pin a released version (newest: {latest})"
                );
                continue;
            }

            if (!packagesAt.TryGetValue(version, out var shipped))
                packagesAt[version] = shipped = PackagesAt($"v{version}");
            if (shipped.Contains(id))
                continue;

            if (packagesAtLatest.Contains(id))
                offenders.Add(
                    $"{where}  {id} {version}: {id} first shipped after {version}; pin {latest}"
                );
            else if (!packagesAtHead.Contains(id))
                offenders.Add($"{where}  {id} {version}: no packable project builds {id}");
            // Otherwise no release has shipped it yet; the next one will, and the pin moves then.
        }

        offenders
            .Should()
            .BeEmpty(
                "a page pins a Trax package at a version that shipped it, or a reader copying the "
                    + "page fails restore with NU1102. Bump every pin to the newest release. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    /// <summary>Every Trax package version written on a published or skill page; ADRs are skipped.</summary>
    private static List<(string Where, string Id, string Version)> Pins()
    {
        var found = new List<(string Where, string Id, string Version)>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var page = RepoRoot.Relative(file).Replace('\\', '/');
            // ADRs quote old versions as history; they are not instructions to install anything.
            if (page.StartsWith("adr/", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in TraxReference.Matches(lines[i]))
                    found.Add(($"{page}:{i + 1}", m.Groups["id"].Value, m.Groups["version"].Value));
                foreach (Match m in TraxCommand.Matches(lines[i]))
                    // A placeholder such as `--version <the Trax version>` names no version.
                    if (!m.Groups["version"].Value.StartsWith('<'))
                        found.Add(
                            ($"{page}:{i + 1}", m.Groups["id"].Value, m.Groups["version"].Value)
                        );
            }
        }

        return found;
    }

    /// <summary>The package ids <paramref name="treeish"/> builds: its packable package projects.</summary>
    private static IReadOnlySet<string> PackagesAt(string treeish)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Git("ls-tree", "-r", "--name-only", treeish))
        {
            var m = PackageProject.Match(path);
            if (!m.Success)
                continue;
            var project = string.Join('\n', Git("show", $"{treeish}:{path}"));
            if (!project.Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal))
                ids.Add(m.Groups["id"].Value);
        }
        return ids;
    }

    private static List<string> Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot.Monorepo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var git = Process.Start(start)!;
        var stderr = git.StandardError.ReadToEndAsync();
        var lines = new List<string>();
        while (git.StandardOutput.ReadLine() is { } line)
            lines.Add(line);
        git.WaitForExit();
        if (git.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed ({git.ExitCode}): {stderr.Result}"
            );
        return lines;
    }
}
