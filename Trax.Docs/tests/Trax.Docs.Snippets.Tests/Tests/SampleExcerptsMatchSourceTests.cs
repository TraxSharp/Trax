namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every fence marked <c>from=path[#region]</c> is an excerpt of that file as it is in this
/// commit.
///
/// <para>A sample tour quotes the sample, and a quote drifts the first time someone edits the
/// sample without opening the page. The file is compiled and tested by its own project; this
/// holds the page to the file, so the page shows what the sample does. The fence may leave lines
/// out, at a line reading <c>// ...</c> (<c># ...</c>, <c>&lt;!-- ... --&gt;</c>), and may drop the
/// indentation the excerpt had; it may not change what it keeps. See <see cref="Excerpts"/>.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class SampleExcerptsMatchSourceTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    [Test]
    public void Every_FromFence_IsAnExcerptOfItsFile()
    {
        var offenders = Fences
            .OnPublishedPages()
            .Where(f => f.From is not null)
            .Select(Excerpts.Problem)
            .OfType<string>()
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a fence marked `from=<path>[#region]` must be lines of that file (the path is "
                    + "relative to the repository root, the folder holding Trax.Docs), split into "
                    + "chunks at `// ...` lines, each chunk consecutive lines of the file in the "
                    + "order the fence shows them. Copy the lines again from the file, or move the "
                    + "`#region` markers. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
