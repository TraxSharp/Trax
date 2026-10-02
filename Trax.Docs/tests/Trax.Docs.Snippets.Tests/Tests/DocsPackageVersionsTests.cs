using System.Xml.Linq;

namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// A Trax package version written on a page is the version the compiled snippets were checked
/// against.
///
/// <para>A reader copies the csproj along with the code, so a page that compiles against one
/// release and tells the reader to install another has checked nothing. A floating
/// <c>Version="1.*"</c> is refused for the same reason, and for the one in
/// <c>Trax.Docs/adr/0002-cross-repo-dependencies-are-exact-pinned.md</c>: it restores whatever was
/// published last, which a later release has already broken once.</para>
///
/// <para>When Dependabot bumps a Trax pin in Directory.Packages.props, this fails until the
/// pages show the new version, which is the point at which their snippets have been re-checked.</para>
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

    [Test]
    public void TraxVersionsOnPages_MatchThePublishedPins()
    {
        var pins = Pins();
        var offenders = new List<string>();

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
                {
                    var id = m.Groups["id"].Value;
                    var version = m.Groups["version"].Value;
                    if (!pins.TryGetValue(id, out var pinned))
                        offenders.Add(
                            $"{page}:{i + 1}  {id} {version}: not pinned in Directory.Packages.props, so nothing checks it"
                        );
                    else if (version != pinned)
                        offenders.Add($"{page}:{i + 1}  {id} {version}: the pin is {pinned}");
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "a page names the exact Trax version its snippets are compiled against, the pin in "
                    + "Directory.Packages.props. After a pin bump, "
                    + "update the pages to it; for a package no snippet uses yet, add its pin. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static Dictionary<string, string> Pins()
    {
        var props = XDocument.Load(RepoRoot.Combine("Directory.Packages.props"));

        return props
            .Descendants("PackageVersion")
            .Select(e =>
                (Id: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version"))
            )
            .Where(p =>
                p.Id is not null
                && p.Version is not null
                && p.Id.StartsWith("Trax.", StringComparison.Ordinal)
            )
            .ToDictionary(p => p.Id!, p => p.Version!, StringComparer.Ordinal);
    }
}
