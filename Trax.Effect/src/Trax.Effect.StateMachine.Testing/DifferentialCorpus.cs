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
        where TTrigger : struct, Enum => CorpusReplay.Replay(machine, goldenJson);
}
