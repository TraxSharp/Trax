namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Facts about the state-machine pages a developer working only from traxsharp.net needs and once could not find:
/// that an invoked train needs a scheduler to dispatch it, the wire form of a comparison and of a constant set
/// step, and the namespace every <c>AddDomainDataContext</c> snippet needs.
///
/// <para>Not ADR-enforcing: it pins page content an outside build found missing, not a recorded decision.</para>
/// </summary>
[TestFixture]
public class StateMachinePagesTests
{
    private static string Page(string relative) =>
        File.ReadAllText(RepoRoot.Combine(relative.Split('/'))).Replace("\r\n", "\n");

    [Test]
    public void Invoking_trains_says_a_scheduler_must_dispatch_the_run()
    {
        Page("statemachine/invoking-trains.md")
            .Should()
            .Contain(
                "a scheduler on this host or another host sharing the database",
                "entering an invoking state only queues the run; without a scheduler the instance waits forever"
            );
    }

    [Test]
    public void Ir_format_gives_the_wire_form_of_a_comparison_and_a_constant()
    {
        var page = Page("sdk-reference/statemachine-api/ir-format.md");

        page.Should().Contain("\"op\": \"lt\"").And.Contain("`gt`, `gte`, `lt`, `lte` or `eq`");
        page.Should().Contain("{ \"const\":");
    }

    [TestCase("effect/effect-providers/domain-data-contexts.md")]
    [TestCase("effect/move-a-domain-data-context-to-ef-migrations.md")]
    public void Every_AddDomainDataContext_snippet_imports_its_namespace(string relative)
    {
        var page = Page(relative);
        var blocks = Regex.Matches(page, "```csharp[^\n]*\n(?<body>.*?)```", RegexOptions.Singleline);

        blocks
            .Select(b => b.Groups["body"].Value)
            .Where(body => body.Contains("AddDomainDataContext<", StringComparison.Ordinal))
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(
                body =>
                    body.Contains(
                        "using Trax.Effect.Data.Services.DomainContext;",
                        StringComparison.Ordinal
                    ),
                "a snippet copied on its own must compile"
            );
    }
}
