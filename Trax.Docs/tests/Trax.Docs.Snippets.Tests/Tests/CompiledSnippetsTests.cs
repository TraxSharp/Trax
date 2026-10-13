namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every fence marked <c>```csharp compile</c> compiles against the Trax projects of the same
/// checkout.
///
/// <para>A page can be well formed and still wrong: an example that calls a method the package
/// does not have, or leaves out the package that has it, passes every other lint and fails the
/// first reader who copies it. <c>compile</c> is one of three markers a C# fence carries
/// (<see cref="EveryCSharpFenceIsMarkedTests"/>): a fragment with an ellipsis says
/// <c>fragment</c>, and a page that claims to be complete marks its fences <c>compile</c> and is
/// held to it.</para>
///
/// <para>The context files a page compiles with are layered (<see cref="SnippetCompiler"/>), and
/// the shared layers imply no Trax namespace, so a page still shows its own usings.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c> and
/// <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class CompiledSnippetsTests
{
    private static IEnumerable<TestCaseData> Units() =>
        SnippetUnit.InDocs().Select(unit => new TestCaseData(unit).SetName(unit.ToString()));

    [TestCaseSource(nameof(Units))]
    public void Snippet_Compiles(SnippetUnit unit)
    {
        var diagnostics = SnippetCompiler.Diagnose(unit);

        diagnostics
            .Should()
            .BeEmpty(
                "a fence marked `compile` must build, warnings included, against the Trax "
                    + "projects of this checkout, which Trax.Docs.Snippets.Tests references as "
                    + "projects. Fix the snippet, or the page if it shows an API the code does not "
                    + "have. Snippets compile with the implicit usings of a Web SDK project, the "
                    + "host variables in Context/_shared.cs and the page's context files, so a "
                    + "missing Trax `using` is the page's to show. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. Diagnostics:\n  "
                    + string.Join("\n  ", diagnostics)
            );
    }

    [Test]
    public void GettingStarted_IsCompiled()
    {
        // The walkthrough is the page a new reader copies from first. If a refactor of the
        // markers drops it out of the compiled set, this says so rather than going quietly green.
        SnippetUnit.InDocs().Should().Contain(u => u.Page == "getting-started.md");
    }

    [Test]
    public void Marker_IsOnlyOnCSharpFences()
    {
        var offenders = Fences
            .InDocs()
            .Where(f => f.IsCompiled && !f.IsCSharp)
            .Select(f => $"{f.Location}  (language '{f.Language}')")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "only C# is compiled, so `compile` on another language would read as checked when "
                    + "nothing checks it. See Trax.Docs/adr/0008-documentation-conventions-are-linted.md. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void ContextFiles_BelongToPagesWithCompiledSnippets()
    {
        var root = SnippetCompiler.ContextRoot;
        var pages = SnippetUnit.InDocs().Select(u => u.Page).ToHashSet(StringComparer.Ordinal);

        var orphans = Directory.Exists(root)
            ? Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(file => !Serves(file, pages))
                .ToList()
            : [];

        orphans
            .Should()
            .BeEmpty(
                "a context file supplies declarations for compiled snippets: `<page path>.cs` for "
                    + "one page, `<dir>/_section.cs` for every page under that directory, "
                    + "`_shared.cs` for all of them. One whose page compiles nothing, or a section "
                    + "file with no compiled page under it, is left over from a rename. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. Orphans (relative "
                    + "to Context/):\n  "
                    + string.Join("\n  ", orphans)
            );
    }

    [Test]
    public void SharedContext_ImpliesNoTraxNamespace()
    {
        var root = SnippetCompiler.ContextRoot;
        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f =>
                f == SnippetCompiler.SharedFile
                || (
                    Path.GetFileName(f) == SnippetCompiler.SectionFile
                    && !f.StartsWith("sdk-reference/", StringComparison.Ordinal)
                )
            )
            .SelectMany(f =>
                File.ReadAllLines(Path.Combine(root, f))
                    .Select((line, i) => (Line: line, At: $"Context/{f}:{i + 1}"))
                    .Where(x => TraxGlobalUsing.IsMatch(x.Line))
                    .Select(x => $"{x.At}  {x.Line.Trim()}")
            )
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a tutorial or how-to page shows the Trax usings a reader has to write, so the "
                    + "context every such page compiles with (`_shared.cs`, and `_section.cs` "
                    + "outside sdk-reference/) holds none. A page's own context file may, and so "
                    + "may an sdk-reference section, whose pages each document one namespace. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private static readonly Regex TraxGlobalUsing = new(
        @"^\s*global\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?Trax\b",
        RegexOptions.Compiled
    );

    /// <summary>Whether a context file, relative to Context/, joins some compiled page.</summary>
    private static bool Serves(string file, IReadOnlySet<string> pages)
    {
        if (file == SnippetCompiler.SharedFile)
            return true;

        if (Path.GetFileName(file) == SnippetCompiler.SectionFile)
        {
            var dir = file[..^SnippetCompiler.SectionFile.Length];
            return pages.Any(p => p.StartsWith(dir, StringComparison.Ordinal));
        }

        return pages.Contains(Path.ChangeExtension(file, ".md"));
    }
}
