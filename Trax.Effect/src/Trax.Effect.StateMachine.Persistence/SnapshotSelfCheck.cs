namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Runs every registered machine's differential self-check (<see cref="IMachine.SelfCheck"/>) and aggregates
/// the diffs. This is the runtime counterpart of the CI differential test: a host calls it at startup (from a
/// health check or a hosted service, injecting the discovered <see cref="IMachine"/>s) to prove the running C#
/// engine still reproduces each machine's committed corpus. Empty result == every machine agrees (or ships no
/// corpus, which is skipped). A machine with no corpus is not a failure — only a genuine divergence is.
/// </summary>
public static class SnapshotSelfCheck
{
    /// <summary>Self-check every machine that ships a corpus; returns one diff per divergent case, machine-prefixed.</summary>
    public static IReadOnlyList<string> Run(IEnumerable<IMachine> machines)
    {
        var diffs = new List<string>();
        foreach (var machine in machines)
        {
            if (machine.Corpus is null)
                continue;
            foreach (var diff in machine.SelfCheck())
                diffs.Add($"{machine.Name}: {diff}");
        }
        return diffs;
    }
}
