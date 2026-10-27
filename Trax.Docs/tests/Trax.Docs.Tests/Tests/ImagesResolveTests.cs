namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every image on a published page is a figure from <c>Trax.Docs/images</c>, it exists, it has
/// alt text, and every figure there is shown somewhere.
///
/// <para><c>Trax.Website/scripts/sync-docs.sh</c> copies <c>images/</c> to the site's
/// <c>/docs-images/</c>, and the site's Content-Security-Policy loads images from its own origin
/// only, so an image anywhere else renders as a broken box. A missing file reads exactly like a
/// present one in a diff, and an image without alt text is invisible to a screen reader and to
/// the agents that read the docs as markdown.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class ImagesResolveTests
{
    private const string ImagesPath = "/docs-images/";

    // A markdown image: ![alt](target) or ![alt](<target> "title").
    private static readonly Regex Image = new(
        @"!\[(?<alt>[^\]]*)\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+""[^""]*"")?\s*\)",
        RegexOptions.Compiled
    );

    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);

    private static readonly Regex InlineCode = new(@"`[^`]*`", RegexOptions.Compiled);

    [Test]
    public void Every_Image_IsAFigure_ThatExists_WithAltText()
    {
        var offenders = new List<string>();

        foreach (var page in RepoRoot.PublishedPages())
        {
            foreach (var (line, alt, target) in Images(ReadPage(page)))
            {
                if (!target.StartsWith(ImagesPath, StringComparison.Ordinal))
                    offenders.Add($"{page}:{line} -> {target} (not under {ImagesPath})");
                else if (!File.Exists(RepoRoot.Combine("images", target[ImagesPath.Length..])))
                    offenders.Add($"{page}:{line} -> {target} (no such file in images/)");

                if (string.IsNullOrWhiteSpace(alt))
                    offenders.Add($"{page}:{line} -> {target} (no alt text)");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "an image on a published page is written ![what it shows](/docs-images/<file>), and "
                    + "<file> is in Trax.Docs/images, which sync-docs.sh copies to the site. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void Every_FigureInImages_IsShownOnAPage()
    {
        var dir = RepoRoot.Combine("images");
        if (!Directory.Exists(dir))
            return;

        var shown = RepoRoot
            .PublishedPages()
            .SelectMany(page => Images(ReadPage(page)))
            .Select(image => image.Target)
            .Where(target => target.StartsWith(ImagesPath, StringComparison.Ordinal))
            .Select(target => target[ImagesPath.Length..])
            .ToHashSet(StringComparer.Ordinal);

        var unused = Directory
            .EnumerateFiles(dir)
            .Select(Path.GetFileName)
            .Where(name => !shown.Contains(name!))
            .Order(StringComparer.Ordinal);

        unused
            .Should()
            .BeEmpty(
                "every file in Trax.Docs/images is published to the site; delete a figure no page "
                    + "shows. Unused:\n  "
                    + string.Join("\n  ", unused)
            );
    }

    private static string ReadPage(string page) =>
        File.ReadAllText(RepoRoot.Combine(page.Split('/')));

    /// <summary>The images on a page with their line numbers, skipping code.</summary>
    private static IEnumerable<(int Line, string Alt, string Target)> Images(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence.IsMatch(lines[i]))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            foreach (Match m in Image.Matches(InlineCode.Replace(lines[i], "")))
                yield return (i + 1, m.Groups["alt"].Value, m.Groups["target"].Value);
        }
    }
}
