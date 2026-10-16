namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every export of <c>@traxsharp/state-machine</c>, from each of its entry points, is named on its reference page,
/// <c>sdk-reference/statemachine-api/typescript-package.md</c>.
///
/// <para>The .NET packages are held to this by <c>PublicApiIsDocumentedTests</c>; the npm package has no public API
/// baseline that test reads, so an export, a type above all, could ship with nowhere to learn it but its
/// <c>.d.ts</c>. The exports are read from the package's committed snapshot
/// (<c>Trax.Api.StateMachine/src/__snapshots__/exports.test.ts.snap</c>), which its own
/// <c>exports.test.ts</c> keeps equal to what each entry point exports. An export counts as documented when its name
/// is a word inside a code span on that page. That proves the name appears, not that the page describing it is
/// right.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class TypeScriptExportsAreDocumentedTests
{
    private const string Page = "sdk-reference/statemachine-api/typescript-package.md";

    private static readonly Regex EntryPoint = new(
        "\"(?<entry>@traxsharp/state-machine[^\"]*)\": \\[(?<names>[^\\]]*)\\]",
        RegexOptions.Compiled
    );

    private static readonly Regex Name = new(
        "\"(?<name>[A-Za-z_$][\\w$]*)\"",
        RegexOptions.Compiled
    );

    private static readonly Regex CodeSpan = new("`([^`\\n]+)`", RegexOptions.Compiled);

    [Test]
    public void Every_export_of_the_npm_package_is_named_on_its_reference_page()
    {
        var snapshot = File.ReadAllText(
            System.IO.Path.Combine(
                RepoRoot.Monorepo,
                "Trax.Api.StateMachine",
                "src",
                "__snapshots__",
                "exports.test.ts.snap"
            )
        );
        var exports = EntryPoint
            .Matches(snapshot)
            .SelectMany(entry =>
                Name.Matches(entry.Groups["names"].Value)
                    .Select(n => (Entry: entry.Groups["entry"].Value, Name: n.Groups["name"].Value))
            )
            .ToList();
        exports
            .Select(e => e.Entry)
            .Distinct()
            .Should()
            .HaveCount(3, "the package has three entry points, and the snapshot was read");

        var words = CodeSpan
            .Matches(File.ReadAllText(RepoRoot.Combine(Page)))
            .SelectMany(span => Regex.Matches(span.Groups[1].Value, "[A-Za-z_$][\\w$]*"))
            .Select(word => word.Value)
            .ToHashSet(StringComparer.Ordinal);
        var undocumented = exports
            .Where(e => !words.Contains(e.Name))
            .Select(e => $"{e.Entry}: {e.Name}")
            .ToList();

        undocumented
            .Should()
            .BeEmpty(
                $"every export of @traxsharp/state-machine is named, in a code span, on {Page}. Document each one there, "
                    + "or stop exporting it. See Trax.Docs/adr/0008-documentation-conventions-are-linted.md"
            );
    }
}
