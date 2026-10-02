namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every fence marked <c>```csharp compile</c> compiles against the published Trax packages.
///
/// <para>A page can be well formed and still wrong: an example that calls a method the package
/// does not have, or leaves out the package that has it, passes every other lint and fails the
/// first reader who copies it. Marking is opt in, so a fragment with an ellipsis stays a
/// fragment; a page that claims to be complete marks its fences and is held to it.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
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
                "a fence marked `compile` must build, warnings included, against the published "
                    + "Trax packages pinned in Directory.Packages.props. Fix the snippet, or the "
                    + "page if it shows an API the pinned release does not have. Snippets on a page "
                    + "compile with the implicit usings of a Web SDK project and nothing else, so a "
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
            .Where(f => f.IsMarked && !f.IsCSharp)
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
        var root = RepoRoot.Combine("tests", "Trax.Docs.Snippets.Tests", "Context");
        var pages = SnippetUnit.InDocs().Select(u => u.Page).ToHashSet(StringComparer.Ordinal);

        var orphans = Directory.Exists(root)
            ? Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(f =>
                    Path.ChangeExtension(Path.GetRelativePath(root, f), ".md").Replace('\\', '/')
                )
                .Where(page => !pages.Contains(page))
                .ToList()
            : [];

        orphans
            .Should()
            .BeEmpty(
                "a context file supplies declarations for one page's compiled snippets and is named "
                    + "after that page. One with no page, or whose page compiles nothing, is left over "
                    + "from a rename. See Trax.Docs/adr/0008-documentation-conventions-are-linted.md. "
                    + "Orphans (as the page each would belong to):\n  "
                    + string.Join("\n  ", orphans)
            );
    }
}
