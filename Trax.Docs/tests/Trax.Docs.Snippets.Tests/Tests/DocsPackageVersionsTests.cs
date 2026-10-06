namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every Trax package version written on a page is one exact version, the same for every package.
///
/// <para>Every Trax package releases at one version
/// (<c>Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md</c>), and the
/// site is published from a release, so a page that tells a reader to install two different
/// versions names at least one that was never released alongside the code the snippets were
/// compiled against. A floating <c>Version="1.*"</c> is refused too: it restores whatever was
/// published last, which a later release has already broken once.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class DocsPackageVersionsTests
{
    private static readonly Regex TraxReference = new(
        @"<PackageReference\s+Include=""(?<id>Trax\.[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.Compiled
    );

    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    [Test]
    public void TraxVersionsOnPages_AreOneExactVersion()
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
                foreach (Match m in TraxReference.Matches(lines[i]))
                    found.Add(($"{page}:{i + 1}", m.Groups["id"].Value, m.Groups["version"].Value));
        }

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
}
