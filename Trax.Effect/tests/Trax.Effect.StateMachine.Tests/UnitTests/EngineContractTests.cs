using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Trax.Effect.StateMachine.Testing;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// One engine contract, the C# half: <c>Advance</c> takes only the triggers a caller sends. An invoked train's
/// outcome and a state's timer are refused there and applied through entry points of their own, and the corpus
/// replays each kind through its own entry point. The TypeScript twin asserts the same in its own suite.
/// </summary>
[Property("adr", TriggerInputValidationTests.Adr)]
public class EngineContractTests
{
    private const string Adr = TriggerInputValidationTests.Adr;

    private static Snapshot Fetching() =>
        new()
        {
            Machine = "ingest",
            Version = 1,
            State = nameof(IngestState.Fetching),
            Context = new JsonObject { ["source"] = IngestMachine.InitialSource },
        };

    private static JsonNode? Output(string fingerprint, bool unsure) =>
        IngestMachine
            .Built.Invokes[IngestState.Fetching]
            .SerializeOutput(new FetchOutput { Fingerprint = fingerprint, Unsure = unsure });

    [Test]
    public void Advance_refuses_an_outcome_trigger_in_both_engines()
    {
        var output = Output("sha256:abc", unsure: false);

        IngestMachine
            .Machine.Advance(Fetching(), "Fetching.done", output)
            .Should()
            .BeOfType<AdvanceResult.Rejected>(
                $"an outcome is applied only by the outcome path, never by an advance ({Adr})"
            );
        IngestMachine.Machine.CanFire(Fetching(), "Fetching.done", output).Should().BeFalse();

        IngestMachine
            .Machine.ApplyOutcome(Fetching(), new InvokeOutcome.Done(output))
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Which.Snapshot.State.Should()
            .Be(nameof(IngestState.Fetched));
    }

    [Test]
    public void Advance_refuses_a_timer_trigger_in_both_engines()
    {
        var review = ReminderMachine.At(ReminderState.Review);

        ReminderMachine
            .Machine.Advance(review, "Review.after")
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.TimerBound,
                $"a timer is fired only by the server, never by an advance ({Adr})"
            );

        ReminderMachine
            .Machine.AdvanceTimer(review, "Review.after")
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Which.Snapshot.State.Should()
            .Be(nameof(ReminderState.Expired));
    }

    // Green today: the C# replay already routes an outcome case away from Advance, which refuses it. A
    // transitioned outcome case can only be reproduced through the outcome entry point.
    [Test]
    public void The_corpus_replays_outcome_cases_through_the_outcome_entry_point()
    {
        var output = Output("sha256:abc", unsure: false);
        var applied = IngestMachine.Machine.ApplyOutcome(
            Fetching(),
            new InvokeOutcome.Done(output)
        );
        var wire = IngestMachine.Machine.Serialize(((AdvanceResult.Transitioned)applied).Snapshot);
        var corpus = new JsonObject
        {
            ["machine"] = "ingest",
            ["version"] = 1,
            ["cases"] = new JsonArray(
                new JsonObject
                {
                    ["given"] = new JsonObject
                    {
                        ["machine"] = "ingest",
                        ["version"] = 1,
                        ["state"] = nameof(IngestState.Fetching),
                        ["context"] = new JsonObject { ["source"] = IngestMachine.InitialSource },
                    },
                    ["when"] = new JsonObject
                    {
                        ["trigger"] = "Fetching.done",
                        ["input"] = output?.DeepClone(),
                    },
                    ["expect"] = new JsonObject { ["outcome"] = "transitioned", ["wire"] = wire },
                }
            ),
        };

        var diffs = DifferentialCorpus.Replay(IngestMachine.Machine, corpus.ToJsonString());

        diffs
            .Should()
            .BeEmpty(
                $"an outcome case replays through the outcome entry point, not Advance ({Adr}): "
                    + string.Join("\n", diffs)
            );
    }

    [Test]
    public void An_outcome_output_that_fails_its_schema_is_refused_not_thrown()
    {
        var output = new JsonObject { ["fingerprint"] = 5, ["unsure"] = "yes" };

        AdvanceResult result = null!;
        var apply = () =>
            result = IngestMachine.Machine.ApplyOutcome(Fetching(), new InvokeOutcome.Done(output));

        apply.Should().NotThrow();
        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"an output that does not match the train's output schema is refused like a bad input ({Adr})"
            );
    }
}
