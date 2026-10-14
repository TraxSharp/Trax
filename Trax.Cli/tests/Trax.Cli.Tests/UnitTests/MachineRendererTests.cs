using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine show</c> draws a machine from its IR. The renderer is pure, so these pin its output from IR
/// text: the turnstile the other machine tests use, and a hand-written IR with every mark the text form has.
/// </summary>
public class MachineRendererTests
{
    private static readonly string Turnstile = new DeclarativeTurnstileMachine().ExportIr();

    // Every mark: initial, committed, a guard with and without Because, a reducer, an effect edge, a state
    // unreachable from the initial state, and a state with no outgoing edge.
    private const string Checkout = """
        {
          "id": "checkout", "version": 2, "initialState": "Cart",
          "states": ["Cart", "Review", "Placed", "Archived"],
          "committedStates": ["Placed"],
          "transitions": [
            { "from": "Cart", "trigger": "Review", "to": "Review",
              "guard": { "rule": "count", "source": "context", "field": "items", "op": "gt", "value": 0 },
              "guardMessage": "Your cart is empty" },
            { "from": "Review", "trigger": "Edit", "to": "Cart",
              "guard": { "rule": "present", "source": "input", "field": "note" },
              "reduce": { "reduce": "clear" } },
            { "from": "Review", "trigger": "Place", "to": "Placed",
              "effect": { "type": "Shop.Payments.IChargeCard", "keyPrefix": "checkout:charge" } },
            { "from": "Archived", "trigger": "Restore", "to": "Cart" }
          ]
        }
        """;

    [Test]
    public void Text_shows_every_state_with_its_edges_and_marks()
    {
        MachineRenderer
            .Render(Checkout, MachineFormat.Text)
            .Should()
            .Be(
                """
                checkout  v2   initial: Cart   committed: Placed

                Cart ◀ initial
                  ── Review ───▶ Review   guard: Your cart is empty

                Review
                  ── Edit ─────▶ Cart     guard: present   reduce: clear
                  ── Place ────▶ Placed   ⚡ effect IChargeCard (send only)

                Placed ■ committed
                  (no transitions)

                Archived unreachable from the initial state
                  ── Restore ──▶ Cart

                """.ReplaceLineEndings("\n")
            );
    }

    [Test]
    public void Text_of_the_turnstile()
    {
        MachineRenderer
            .Render(Turnstile, MachineFormat.Text)
            .Should()
            .Be(
                """
                turnstile  v1   initial: Locked

                Locked ◀ initial
                  ── Coin ──▶ Unlocked   guard: Only a quarter or a dollar is accepted.   reduce: set

                Unlocked
                  ── Push ──▶ Locked     reduce: clear

                """.ReplaceLineEndings("\n")
            );
    }

    [Test]
    public void Ascii_draws_without_box_glyphs()
    {
        var text = MachineRenderer.Render(Checkout, MachineFormat.Text, ascii: true);

        text.Should().Contain("Cart <- initial").And.Contain("-- Place ----> Placed");
        text.Should().Contain("! effect IChargeCard").And.Contain("Placed [x] committed");
        text.Where(c => c > 127).Should().BeEmpty();
    }

    [Test]
    public void Colour_is_written_only_when_asked_for()
    {
        MachineRenderer.Render(Checkout, MachineFormat.Text).Should().NotContain("\u001b[");
        MachineRenderer
            .Render(Checkout, MachineFormat.Text, color: true)
            .Should()
            .Contain("\u001b[");
    }

    [Test]
    public void Mermaid_is_a_state_diagram()
    {
        MachineRenderer
            .Render(Checkout, MachineFormat.Mermaid)
            .Should()
            .Be(
                """
                stateDiagram-v2
                    [*] --> Cart
                    Cart --> Review : Review / guard#58; Your cart is empty
                    Review --> Cart : Edit / guard#58; present / reduce#58; clear
                    Review --> Placed : Place / effect IChargeCard (send only)
                    Archived --> Cart : Restore
                    classDef committed font-weight:bold,stroke-width:3px
                    class Placed committed

                """.ReplaceLineEndings("\n")
            );
    }

    [Test]
    public void Dot_is_a_digraph()
    {
        MachineRenderer
            .Render(Turnstile, MachineFormat.Dot)
            .Should()
            .Be(
                """
                digraph "turnstile" {
                    rankdir=LR;
                    node [shape=box, style=rounded];
                    __initial [shape=point, label=""];
                    __initial -> "Locked";
                    "Locked";
                    "Unlocked";
                    "Locked" -> "Unlocked" [label="Coin\nguard: Only a quarter or a dollar is accepted.\nreduce: set"];
                    "Unlocked" -> "Locked" [label="Push\nreduce: clear"];
                }

                """.ReplaceLineEndings("\n")
            );
    }

    // Review is held for ten minutes, then the server moves it to Expired. A drawing that left the timer out
    // would show Review as a state only a user's event leaves.
    private static readonly string Reminder = new DeclarativeReminderMachine().ExportIr();

    // The timer edge Review → Expired, labelled with its duration as the IR spells it or as a reader would.
    private static readonly Regex TimerDuration = new(
        @"\b(PT10M|10m|10 min)",
        RegexOptions.Compiled
    );

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public void Text_marks_a_timer_edge_with_its_duration()
    {
        var lines = MachineRenderer.Render(Reminder, MachineFormat.Text).Split('\n');

        lines
            .Should()
            .Contain(
                l => l.Contains("▶ Expired", StringComparison.Ordinal) && TimerDuration.IsMatch(l),
                "Review's timer is an edge to Expired, marked with how long Review is held "
                    + "(Trax.Docs/adr/0048)"
            );
    }

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public void Mermaid_marks_a_timer_edge_with_its_duration()
    {
        var lines = MachineRenderer.Render(Reminder, MachineFormat.Mermaid).Split('\n');

        lines
            .Should()
            .Contain(
                l =>
                    l.Contains("Review --> Expired", StringComparison.Ordinal)
                    && TimerDuration.IsMatch(l),
                "Review's timer is an edge to Expired, labelled with its duration (Trax.Docs/adr/0048)"
            );
    }

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public void Dot_marks_a_timer_edge_with_its_duration()
    {
        var lines = MachineRenderer.Render(Reminder, MachineFormat.Dot).Split('\n');

        lines
            .Should()
            .Contain(
                l =>
                    l.Contains("\"Review\" -> \"Expired\"", StringComparison.Ordinal)
                    && TimerDuration.IsMatch(l),
                "Review's timer is an edge to Expired, labelled with its duration (Trax.Docs/adr/0048)"
            );
    }

    // Dealing invokes a train: its outcomes are the only way out of Dealing and the only way into Guessing and
    // Lost, so a renderer that read transitions alone would call both unreachable.
    private const string Cards = """
        {
          "id": "cards", "version": 1, "initialState": "Idle",
          "states": ["Idle", "Dealing", "Guessing", "Lost"],
          "transitions": [ { "from": "Idle", "trigger": "Deal", "to": "Dealing" } ],
          "outcomes": {
            "Dealing.done": { "state": "Dealing", "outcome": "done", "train": "Cards.Deal",
              "edges": [ { "to": "Guessing",
                           "guard": { "rule": "compare", "source": "input", "field": "rank", "op": "gt", "value": 0 } },
                         { "to": "Lost" } ] },
            "Dealing.failed": { "state": "Dealing", "outcome": "failed", "train": "Cards.Deal",
              "edges": [ { "to": "Lost" } ] },
            "Dealing.cancelled": { "state": "Dealing", "outcome": "cancelled", "train": "Cards.Deal",
              "edges": [] }
          }
        }
        """;

    [Test]
    public void Outcome_targets_are_reachable()
    {
        var text = MachineRenderer.Render(Cards, MachineFormat.Text);

        text.Should().NotContain("unreachable");
        text.Should()
            .Be(
                """
                cards  v1   initial: Idle

                Idle ◀ initial
                  ── Deal ────────────▶ Dealing

                Dealing
                  ── Dealing.done ────▶ Guessing   guard: compare
                  ── Dealing.done ────▶ Lost
                  ── Dealing.failed ──▶ Lost

                Guessing
                  (no transitions)

                Lost
                  (no transitions)

                """.ReplaceLineEndings("\n")
            );
    }

    [TestCase("mermaid", "Dealing --> Guessing : Dealing.done")]
    [TestCase("dot", "\"Dealing\" -> \"Guessing\" [label=\"Dealing.done")]
    public void Diagrams_draw_outcome_edges(string name, string edge)
    {
        var format = name == "dot" ? MachineFormat.Dot : MachineFormat.Mermaid;

        MachineRenderer.Render(Cards, format).Should().Contain(edge);
    }

    // Scoring's outcome has several guarded `done` edges, tried in order, two of which lead to Won; its `failed`
    // edge leads to Won too, so Scoring -> Won is drawn three times, once per edge, each with its own label.
    private const string Scores = """
        {
          "id": "scores", "version": 2, "initialState": "Idle",
          "states": ["Idle", "Scoring", "Won", "Lost"],
          "transitions": [ { "from": "Idle", "trigger": "Play", "to": "Scoring" } ],
          "outcomes": {
            "Scoring.done": { "state": "Scoring", "outcome": "done", "train": "Game.Score",
              "edges": [ { "to": "Won",
                           "guard": { "rule": "compare", "source": "input", "field": "points", "op": "gte", "value": 100 } },
                         { "to": "Won",
                           "guard": { "rule": "present", "source": "input", "field": "bonus" },
                           "reduce": { "reduce": "merge" } },
                         { "to": "Lost",
                           "guard": { "rule": "compare", "source": "input", "field": "points", "op": "lt", "value": 10 } },
                         { "to": "Idle" } ] },
            "Scoring.failed": { "state": "Scoring", "outcome": "failed", "train": "Game.Score",
              "edges": [ { "to": "Won" } ] }
          }
        }
        """;

    [Test]
    public void Several_guarded_done_edges_are_listed_in_the_order_they_are_tried()
    {
        var text = MachineRenderer.Render(Scores, MachineFormat.Text);

        text.Should()
            .Be(
                """
                scores  v2   initial: Idle

                Idle ◀ initial
                  ── Play ────────────▶ Scoring

                Scoring
                  ── Scoring.done ────▶ Won       guard: compare
                  ── Scoring.done ────▶ Won       guard: present   reduce: merge
                  ── Scoring.done ────▶ Lost      guard: compare
                  ── Scoring.done ────▶ Idle
                  ── Scoring.failed ──▶ Won

                Won
                  (no transitions)

                Lost
                  (no transitions)

                """.ReplaceLineEndings("\n")
            );
    }

    [Test]
    public void Mermaid_draws_each_edge_between_the_same_two_states()
    {
        var mermaid = MachineRenderer.Render(Scores, MachineFormat.Mermaid);

        mermaid
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("Scoring --> Won", StringComparison.Ordinal))
            .Should()
            .Equal(
                "    Scoring --> Won : Scoring.done / guard#58; compare",
                "    Scoring --> Won : Scoring.done / guard#58; present / reduce#58; merge",
                "    Scoring --> Won : Scoring.failed"
            );
        mermaid.Should().Contain("    Scoring --> Idle : Scoring.done\n");
    }

    [Test]
    public void Dot_draws_each_edge_between_the_same_two_states()
    {
        var dot = MachineRenderer.Render(Scores, MachineFormat.Dot);

        dot.Split('\n')
            .Where(line =>
                line.TrimStart().StartsWith("\"Scoring\" -> \"Won\"", StringComparison.Ordinal)
            )
            .Should()
            .Equal(
                "    \"Scoring\" -> \"Won\" [label=\"Scoring.done\\nguard: compare\"];",
                "    \"Scoring\" -> \"Won\" [label=\"Scoring.done\\nguard: present\\nreduce: merge\"];",
                "    \"Scoring\" -> \"Won\" [label=\"Scoring.failed\"];"
            );
    }

    [Test]
    public void Show_prints_the_machine_from_an_assembly()
    {
        using var output = new StringWriter();
        var exit = MachineCommand.RunShow(
            typeof(DeclarativeTurnstileMachine).Assembly.Location,
            typeof(DeclarativeTurnstileMachine).FullName,
            null,
            false,
            output
        );

        exit.Should().Be(0);
        output.ToString().Should().StartWith("turnstile  v1   initial: Locked");
    }

    [Test]
    public void Show_with_several_machines_and_none_named_gives_the_error_generate_does()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        Console.SetError(error);
        int exit;
        try
        {
            exit = MachineCommand.RunShow(
                typeof(DeclarativeTurnstileMachine).Assembly.Location,
                null,
                null,
                false,
                new StringWriter()
            );
        }
        finally
        {
            Console.SetError(original);
        }

        exit.Should().Be(1);
        error.ToString().Should().Contain("pass --machine <FullName> to pick one");
    }

    [Test]
    public void Show_refuses_an_unknown_format()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        Console.SetError(error);
        try
        {
            MachineCommand
                .RunShow(
                    typeof(DeclarativeTurnstileMachine).Assembly.Location,
                    typeof(DeclarativeTurnstileMachine).FullName,
                    "svg",
                    false,
                    new StringWriter()
                )
                .Should()
                .Be(1);
        }
        finally
        {
            Console.SetError(original);
        }
        error.ToString().Should().Contain("--format must be");
    }

    [Test]
    public void Text_does_not_pass_control_characters_from_the_machine_to_the_terminal()
    {
        // A guard message that would clear the screen and a state name that would rewrite the line.
        const string ir = """
            {
              "id": "evil", "version": 1, "initialState": "Start\r",
              "states": ["Start\r", "End"],
              "transitions": [
                { "from": "Start\r", "trigger": "Go\u001b[2J", "to": "End",
                  "guard": { "rule": "present", "source": "input", "field": "x" },
                  "guardMessage": "\u001b]0;pwned\u0007 no" }
              ]
            }
            """;

        var text = MachineRenderer.Render(ir, MachineFormat.Text, color: true);

        text.Should().NotContain("\r").And.NotContain("\u0007").And.NotContain("\u001b[2J");
        text.Should().NotContain("\u001b]0;");
        text.Should().Contain("Start?").And.Contain("Go?[2J").And.Contain("guard: ?]0;pwned? no");
        // The renderer's own colour codes still go out.
        text.Should().Contain("\u001b[1m");
    }

    [TestCase("mermaid")]
    [TestCase("dot")]
    public void Diagram_sources_do_not_pass_control_characters_either(string name)
    {
        var format = name == "dot" ? MachineFormat.Dot : MachineFormat.Mermaid;
        const string ir = """
            {
              "id": "evil", "version": 1, "initialState": "Start",
              "states": ["Start", "End"],
              "transitions": [ { "from": "Start", "trigger": "Go\u001b[2J", "to": "End" } ]
            }
            """;

        var source = MachineRenderer.Render(ir, format);

        source.Should().NotContain("\u001b").And.Contain("Go?[2J").And.Contain("\n");
    }
}
