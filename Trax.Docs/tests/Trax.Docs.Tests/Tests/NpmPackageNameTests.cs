namespace Trax.Docs.Tests.Tests;

/// <summary>
/// The docs name the TypeScript engine by the name it is published under on npm,
/// <c>@traxsharp/state-machine</c>.
///
/// <para>The <c>@trax</c> npm scope belongs to an unrelated owner, so an import or an install
/// line written against it can never resolve to Trax's package. A reader who copies one gets
/// someone else's package or a 404.</para>
///
/// <para>Not ADR-enforcing: it pins a package name that npm decides, not a convention anyone
/// weighed.</para>
/// </summary>
[TestFixture]
public class NpmPackageNameTests
{
    [Test]
    public void DocsMarkdown_NamesNo_PackageInThe_TraxNpmScope()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("@trax/", StringComparison.Ordinal))
                    offenders.Add($"{RepoRoot.Relative(file)}:{i + 1}  -> {lines[i].Trim()}");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "the TypeScript engine is published as @traxsharp/state-machine; the @trax npm "
                    + "scope is someone else's. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
