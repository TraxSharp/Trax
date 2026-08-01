using FluentAssertions;
using Trax.Effect.StateMachine.Testing;
using Trax.Effect.StateMachine.Tests.Fakes;

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
              "expect": { "outcome": "rejected", "reason": "no-transition" }
            },
            {
              "given": { "machine": "turnstile", "version": 1, "state": "Locked", "context": {} },
              "when": { "trigger": "Coin", "input": { "coin": "penny" } },
              "expect": { "outcome": "rejected", "reason": "guard-failed" }
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
    public void Replay_flags_a_case_whose_recorded_reason_disagrees()
    {
        var diffs = DifferentialCorpus.Replay(TestTurnstile.Machine, WrongReasonCorpus);
        diffs.Should().ContainSingle().Which.Should().Contain("Push").And.Contain("no-transition");
    }
}
