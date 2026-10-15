namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Facts about the state-machine pages a developer working only from traxsharp.net needs and once could not find or
/// found wrong: that an invoked train needs a scheduler to dispatch it while a timer on its state fires without one,
/// the wire form of a comparison and of a constant set step, what a comparison refuses and how it differs from a
/// negation, that only the server refuses an effect-bound advance, that a state may take several differential
/// seeds, and the namespace every <c>AddDomainDataContext</c> snippet needs.
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
    public void Invoking_trains_says_a_timer_on_the_invoking_state_fires_without_a_scheduler()
    {
        Page("statemachine/invoking-trains.md")
            .Should()
            .Contain(
                "An `After(...)`\non the invoking state fires with or without a scheduler",
                "the outcome reconciler fires timers on every host with machines; TimerDueAtTests pins it"
            )
            .And.Contain("leaving the state cancels the queued run");
    }

    [Test]
    public void Only_the_server_refuses_an_effect_bound_advance()
    {
        Page("sdk-reference/statemachine-api/machine.md")
            .Should()
            .Contain(
                "The engine's own `SnapshotMachine.Advance` and the TypeScript twin's `advance` do not refuse it",
                "the bare engine fires an effect-bound edge like any other"
            );
        Page("sdk-reference/statemachine-api/i-snapshot-effect.md")
            .Should()
            .Contain(
                "the bare engine does not:\n`SnapshotMachine.Advance` fires the edge like any other"
            );
        Page("sdk-reference/statemachine-api/result-codes.md")
            .Should()
            .Contain("| `EFFECT_BOUND` | advance (server) |")
            .And.Contain("`EFFECT_BOUND` is not among them");
    }

    [Test]
    public void A_state_may_take_several_seeds_and_a_probe_is_not_one()
    {
        Page("sdk-reference/statemachine-api/machine.md")
            .Should()
            .Contain("A state may be seeded more than once, and the search starts from every seed")
            .And.NotContain("add further contexts with `Probe`");
        Page("statemachine/codegen-pipeline.md")
            .Should()
            .Contain(
                "crossed with every state once and never\n  expanded",
                "a probe is not a substitute for a seed: following that advice lost coverage"
            );
        Page("sdk-reference/statemachine-api/ir-format.md")
            .Should()
            .Contain("or a list of contexts when the state is seeded more than once");
    }

    [Test]
    public void Rules_say_what_a_comparison_refuses_and_how_AtMost_differs_from_a_negation()
    {
        var page = Page("sdk-reference/statemachine-api/rules.md");

        page.Should()
            .Contain("`AtMost(12)` is false there, where \"not greater than 12\" would be true")
            .And.Contain("whose schema type is not `number` or `integer`")
            .And.Contain("The bound must be finite")
            .And.Contain("(`99.99999999999999999` reads as `100`)");
    }

    [Test]
    public void Ir_format_says_a_field_is_its_json_name()
    {
        Page("sdk-reference/statemachine-api/ir-format.md")
            .Should()
            .Contain("`field` (the member's JSON name: its\n`[JsonPropertyName]` when it has one")
            .And.NotContain("the camelCase key");
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
    [TestCase("sdk-reference/configuration/domain-data-context.md")]
    [TestCase("api/expose-an-entity-as-a-query-model.md")]
    public void Every_AddDomainDataContext_snippet_imports_its_namespace(string relative)
    {
        var page = Page(relative);
        // An excerpt (from=) mirrors a real file, whose using lines are in that file.
        var blocks = Regex
            .Matches(page, "```csharp(?<marker>[^\n]*)\n(?<body>.*?)```", RegexOptions.Singleline)
            .Where(b =>
                !b.Groups["marker"].Value.TrimStart().StartsWith("from=", StringComparison.Ordinal)
            );

        blocks
            .Select(b => b.Groups["body"].Value)
            .Where(body => body.Contains(".AddDomainDataContext<", StringComparison.Ordinal))
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
