using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Testing;

/// <summary>
/// Replays a shared, TypeScript-generated differential corpus (produced by the <c>@trax/state-machine</c>
/// oracle) through the C# engine. TypeScript enumerates the reachable behavior space and records each
/// outcome as canonical wire (on a transition) or a rejection code; this reproduces every case and returns
/// the ones it fails to match, which is the exhaustive proof that the hand-written twin reducers stay
/// identical (PD1). Only reason codes and canonical wire are compared, never rejection detail (PD7).
///
/// Framework-agnostic: it returns a list of human-readable diffs (empty == exact agreement). A consumer
/// wraps it in one test:
/// <code>
/// [Test]
/// public void Engine_matches_the_oracle()
/// {
///     var diffs = DifferentialCorpus.Replay(MyMachine.Engine, File.ReadAllText(goldenPath));
///     Assert.That(diffs, Is.Empty, string.Join("\n", diffs));
/// }
/// </code>
/// </summary>
public static class DifferentialCorpus
{
    /// <summary>
    /// Replay a committed golden through <paramref name="machine"/>. Returns one human-readable diff per
    /// case the machine does NOT reproduce; an empty list means exact agreement with the oracle.
    /// </summary>
    public static IReadOnlyList<string> Replay<TState, TTrigger>(
        SnapshotMachine<TState, TTrigger> machine,
        string goldenJson
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var doc = (JsonObject)JsonNode.Parse(goldenJson)!;
        var diffs = new List<string>();

        foreach (var node in (JsonArray)doc["cases"]!)
        {
            var c = (JsonObject)node!;
            var given = (JsonObject)c["given"]!;
            var when = (JsonObject)c["when"]!;
            var expect = (JsonObject)c["expect"]!;

            var snap = new Snapshot
            {
                Machine = given["machine"]!.GetValue<string>(),
                Version = given["version"]!.GetValue<int>(),
                State = given["state"]!.GetValue<string>(),
                Context = (JsonObject)given["context"]!.DeepClone(),
            };
            var trigger = when["trigger"]!.GetValue<string>();
            var input = when["input"]?.DeepClone();

            var (outcome, wire, reason) = machine.Advance(snap, trigger, input) switch
            {
                AdvanceResult.Transitioned t => (
                    "transitioned",
                    machine.Serialize(t.Snapshot),
                    (string?)null
                ),
                AdvanceResult.Rejected r => ("rejected", (string?)null, r.Reason),
                _ => ("internal-error", null, null),
            };

            var wantOutcome = expect["outcome"]!.GetValue<string>();
            var wantWire = expect["wire"]?.GetValue<string>();
            var wantReason = expect["reason"]?.GetValue<string>();

            if (outcome != wantOutcome || wire != wantWire || reason != wantReason)
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
