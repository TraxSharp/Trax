using System.ComponentModel;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.StateMachine;

/// <summary>
/// Replays a shared, TypeScript-generated differential corpus (produced by the <c>@traxsharp/state-machine</c>
/// oracle) through the C# engine and returns one human-readable diff per case the machine does NOT reproduce
/// (an empty list means exact agreement). TypeScript enumerates the reachable behaviour space and records
/// each outcome as canonical wire (on a transition) or a rejection code; this reproduces every case. Only
/// reason codes and canonical wire are compared, never rejection detail.
///
/// <para>This lives in the core engine (not the test-only package) so a running host can replay a committed
/// corpus at startup as a self-check, the same proof the differential test runs in CI.</para>
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine-conformance">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CorpusReplay
{
    /// <summary>Replay a committed golden through <paramref name="machine"/>; empty result == exact agreement.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<string> Replay<TState, TTrigger>(
        SnapshotMachine<TState, TTrigger> machine,
        string goldenJson
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var doc = (JsonObject)JsonNode.Parse(goldenJson).NotNull();
        var diffs = new List<string>();

        foreach (var node in (JsonArray)doc["cases"].NotNull())
        {
            var c = (JsonObject)node.NotNull();
            var given = (JsonObject)c["given"].NotNull();
            var when = (JsonObject)c["when"].NotNull();
            var expect = (JsonObject)c["expect"].NotNull();

            var snap = new Snapshot
            {
                Machine = given["machine"].NotNull().GetValue<string>(),
                Version = given["version"].NotNull().GetValue<int>(),
                State = given["state"].NotNull().GetValue<string>(),
                Context = (JsonObject)given["context"].NotNull().DeepClone(),
            };
            var trigger = when["trigger"].NotNull().GetValue<string>();
            var input = when["input"]?.DeepClone();

            // Each trigger kind replays through the entry point that owns it, as the twin enumerates it: an invoked
            // train's outcome (Fetching.done) through the outcome path, a state's timer (Review.after) through
            // AdvanceTimer. Advance refuses both.
            var result =
                machine.IsOutcomeTrigger(trigger) ? machine.AdvanceOutcome(snap, trigger, input)
                : machine.IsTimerTrigger(trigger) ? machine.AdvanceTimer(snap, trigger)
                : machine.Advance(snap, trigger, input);
            var (outcome, wire, reason) = result switch
            {
                AdvanceResult.Transitioned t => (
                    "transitioned",
                    machine.Serialize(t.Snapshot),
                    (string?)null
                ),
                AdvanceResult.Rejected r => ("rejected", (string?)null, r.Reason.ToWire()),
                _ => ("internal-error", null, null),
            };

            var wantOutcome = expect["outcome"].NotNull().GetValue<string>();
            var wantWire = expect["wire"]?.GetValue<string>();
            var wantReason = expect["reason"]?.GetValue<string>();

            if (
                !string.Equals(outcome, wantOutcome, StringComparison.Ordinal)
                || !string.Equals(wire, wantWire, StringComparison.Ordinal)
                || !string.Equals(reason, wantReason, StringComparison.Ordinal)
            )
            {
                var payload = input is null ? "" : " " + input.ToJsonString();
                diffs.Add(
                    $"[{snap.State} + {trigger}{payload}] oracle {wantOutcome} "
                        + $"{wantWire ?? wantReason}, C# {outcome} {wire ?? reason}"
                );
            }
        }
        return diffs;
    }
}
