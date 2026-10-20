using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Testing;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Differential;

/// <summary>
/// Direct unit tests for <see cref="DifferentialCorpus.Replay{TState,TTrigger}"/> over an inline corpus,
/// so the replay logic is covered without the workspace-shared goldens (which are absent in this repo's
/// isolated CI, where the conformance test skips). Covers the transition-match, rejection-match, and both
/// mismatch (wire and reason) paths.
/// </summary>
public class DifferentialCorpusReplayTests
{
    // A minimal turnstile corpus the C# engine reproduces exactly: a guarded transition (with input), a
    // no-transition rejection (no input), and a guard-failed rejection.
    private const string MatchingCorpus = """
        {
          "machine": "turnstile",
          "version": 1,
          "cases": [
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Coin", "input": { "coin": "quarter" } },
              "expect": { "outcome": "transitioned", "wire": "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}" }
            },
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Push" },
              "expect": { "outcome": "rejected", "reason": "NO_TRANSITION" }
            },
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Coin", "input": { "coin": "penny" } },
              "expect": { "outcome": "rejected", "reason": "GUARD_FAILED" }
            }
          ]
        }
        """;

    private const string WrongWireCorpus = """
        {
          "machine": "turnstile",
          "version": 1,
          "cases": [
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Coin", "input": { "coin": "quarter" } },
              "expect": { "outcome": "transitioned", "wire": "WRONG" }
            }
          ]
        }
        """;

    private const string WrongReasonCorpus = """
        {
          "machine": "turnstile",
          "version": 1,
          "cases": [
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Push" },
              "expect": { "outcome": "rejected", "reason": "WRONG" }
            }
          ]
        }
        """;

    [Test]
    public void Replay_returns_no_diffs_when_the_engine_reproduces_every_case()
    {
        var diffs = DifferentialCorpus.Replay(TestTurnstile.Machine, MatchingCorpus);
        diffs.Should().BeEmpty(string.Join("\n", diffs));
    }

    [Test]
    public void Replay_flags_a_case_whose_recorded_wire_disagrees()
    {
        var diffs = DifferentialCorpus.Replay(TestTurnstile.Machine, WrongWireCorpus);
        diffs
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Coin")
            .And.Contain("oracle transitioned WRONG")
            .And.Contain("C# transitioned");
    }

    [Test]
    public void Outcome_triggers_apply_the_same_reduction_in_the_twin()
    {
        // The TypeScript twin enumerates the ingest machine from its IR, firing its outcome triggers
        // (Fetching.done with each sampled output, Fetching.failed, Fetching.cancelled) as events. The C# engine
        // replays those cases through its outcome path and must land in the same state with the same reduced
        // context, byte for byte. This is the pure half only: the twin holds no token, so a stale completion is
        // indistinguishable from a live one here.
        var file = FixturePaths.DifferentialFile("ingest");
        var corpus = File.ReadAllText(file);

        var outcomeCases = JsonNode.Parse(corpus)!["cases"]!
            .AsArray()
            .Where(c => c!["when"]!["trigger"]!.GetValue<string>().StartsWith("Fetching."))
            .ToList();
        outcomeCases
            .Should()
            .Contain(
                c => c!["expect"]!["outcome"]!.GetValue<string>() == "transitioned",
                "the corpus must exercise the outcome reductions, not only their rejections"
            );

        var diffs = DifferentialCorpus.Replay(IngestMachine.Machine, corpus);

        diffs.Should().BeEmpty(string.Join("\n", diffs));
    }

    [Test]
    public void Replay_flags_a_case_whose_recorded_reason_disagrees()
    {
        var diffs = DifferentialCorpus.Replay(TestTurnstile.Machine, WrongReasonCorpus);
        diffs
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Push")
            .And.Contain(RefusalCode.NoTransition.ToWire());
    }

    // A reminder corpus with a timer case: Review fires its timer into Expired; Idle has no timer.
    private const string TimerCorpus = """
        {
          "machine": "reminder",
          "version": 1,
          "cases": [
            {
              "given": { "machine": "reminder", "version": 1, "state": "Review", "context": {} },
              "when": { "trigger": "Review.after" },
              "expect": { "outcome": "transitioned", "wire": "{\"machine\":\"reminder\",\"version\":1,\"state\":\"Expired\",\"context\":{}}" }
            },
            {
              "given": { "machine": "reminder", "version": 1, "state": "Idle", "context": {} },
              "when": { "trigger": "Review.after" },
              "expect": { "outcome": "rejected", "reason": "NO_TRANSITION" }
            }
          ]
        }
        """;

    [Test]
    public void Timer_triggers_replay_through_AdvanceTimer()
    {
        var diffs = DifferentialCorpus.Replay(ReminderMachine.Machine, TimerCorpus);

        diffs
            .Should()
            .BeEmpty(
                "a timer case replays through AdvanceTimer, never Advance (Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md): "
                    + string.Join("\n", diffs)
            );
    }
}
