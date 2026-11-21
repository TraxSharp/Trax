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
public partial class StateMachinePagesTests
{
    private static async Task<string> PageAsync(string relative) =>
        (
            await File.ReadAllTextAsync(RepoRoot.Combine(relative.Split('/'))).ConfigureAwait(false)
        ).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Test]
    public async Task Invoking_trains_says_a_scheduler_must_dispatch_the_run()
    {
        (await PageAsync("statemachine/invoking-trains.md").ConfigureAwait(false))
            .Should()
            .Contain(
                "a scheduler on this host or another host sharing the database",
                "entering an invoking state only queues the run; without a scheduler the instance waits forever"
            );
    }

    [Test]
    public async Task Invoking_trains_says_a_timer_on_the_invoking_state_fires_without_a_scheduler()
    {
        (await PageAsync("statemachine/invoking-trains.md").ConfigureAwait(false))
            .Should()
            .Contain(
                "An `After(...)`\non the invoking state fires with or without a scheduler",
                "the outcome reconciler fires timers on every host with machines; TimerDueAtTests pins it"
            )
            .And.Contain("leaving the state cancels the queued run");
    }

    [Test]
    public async Task Only_the_server_refuses_an_effect_bound_advance()
    {
        (await PageAsync("sdk-reference/statemachine-api/machine.md").ConfigureAwait(false))
            .Should()
            .Contain(
                "The engine's own `SnapshotMachine.Advance` and the TypeScript twin's `advance` do not refuse it",
                "the bare engine fires an effect-bound edge like any other"
            );
        (
            await PageAsync("sdk-reference/statemachine-api/i-snapshot-effect.md")
                .ConfigureAwait(false)
        )
            .Should()
            .Contain(
                "the bare engine does not:\n`SnapshotMachine.Advance` fires the edge like any other"
            );
        (await PageAsync("sdk-reference/statemachine-api/result-codes.md").ConfigureAwait(false))
            .Should()
            .Contain("| `EFFECT_BOUND` | advance (server) |")
            .And.Contain("`EFFECT_BOUND` is not among them");
    }

    [Test]
    public async Task A_state_may_take_several_seeds_and_a_probe_is_not_one()
    {
        (await PageAsync("sdk-reference/statemachine-api/machine.md").ConfigureAwait(false))
            .Should()
            .Contain("A state may be seeded more than once, and the search starts from every seed")
            .And.NotContain("add further contexts with `Probe`");
        (await PageAsync("statemachine/codegen-pipeline.md").ConfigureAwait(false))
            .Should()
            .Contain(
                "crossed with every state once and never\n  expanded",
                "a probe is not a substitute for a seed: following that advice lost coverage"
            );
        (await PageAsync("sdk-reference/statemachine-api/ir-format.md").ConfigureAwait(false))
            .Should()
            .Contain("or a list of contexts when the state is seeded more than once");
    }

    [Test]
    public async Task Rules_say_what_a_comparison_refuses_and_how_AtMost_differs_from_a_negation()
    {
        var page = (
            await PageAsync("sdk-reference/statemachine-api/rules.md").ConfigureAwait(false)
        );

        page.Should()
            .Contain("`AtMost(12)` is false there, where \"not greater than 12\" would be true")
            .And.Contain("whose schema type is not `number` or `integer`")
            .And.Contain("The bound must be finite")
            .And.Contain("(`99.99999999999999999` reads as `100`)");
    }

    [Test]
    public async Task Ir_format_says_a_field_is_its_json_name()
    {
        (await PageAsync("sdk-reference/statemachine-api/ir-format.md").ConfigureAwait(false))
            .Should()
            .Contain("`field` (the member's JSON name: its\n`[JsonPropertyName]` when it has one")
            .And.NotContain("the camelCase key");
    }

    [Test]
    public async Task Ir_format_gives_the_wire_form_of_a_comparison_and_a_constant()
    {
        var page = (
            await PageAsync("sdk-reference/statemachine-api/ir-format.md").ConfigureAwait(false)
        );

        page.Should().Contain("\"op\": \"lt\"").And.Contain("`gt`, `gte`, `lt`, `lte` or `eq`");
        page.Should().Contain("{ \"const\":");
    }

    [TestCase("effect/effect-providers/domain-data-contexts.md")]
    [TestCase("effect/move-a-domain-data-context-to-ef-migrations.md")]
    [TestCase("sdk-reference/configuration/domain-data-context.md")]
    [TestCase("api/expose-an-entity-as-a-query-model.md")]
    public async Task Every_AddDomainDataContext_snippet_imports_its_namespace(string relative)
    {
        var page = (await PageAsync(relative).ConfigureAwait(false));
        // An excerpt (from=) mirrors a real file, whose using lines are in that file.
        var blocks = CSharpBlock
            .Matches(page)
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

    [GeneratedRegex(
        "```csharp(?<marker>[^\n]*)\n(?<body>.*?)```",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex CSharpBlock { get; }
}
