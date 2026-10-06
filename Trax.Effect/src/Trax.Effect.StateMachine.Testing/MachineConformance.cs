using Trax.Effect.StateMachine.Persistence;

namespace Trax.Effect.StateMachine.Testing;

/// <summary>
/// The three checks that keep a machine, its committed artifacts, and the generated frontend twin from
/// drifting apart. Every machine with a generated twin needs all three, and they are easy to write as three
/// separate tests with three copies of the same path plumbing — so they live here instead.
///
/// Each check catches something the other two structurally cannot:
///
/// <list type="bullet">
/// <item><b>IR</b> — the committed <c>*.ir.json</c> still equals <see cref="IMachine.ExportIr"/>. That file
/// is what the twin is generated FROM, so this catches a machine edit that skipped regeneration.</item>
/// <item><b>corpus</b> — the committed <c>differential.json</c> still equals the copy EMBEDDED in the
/// assembly. Catches a rebuild that shipped a stale corpus, which would make the self-check below pass
/// against the wrong golden.</item>
/// <item><b>self-check</b> — the embedded corpus replays clean through this build's engine
/// (<see cref="IMachine.SelfCheck"/>). Catches an engine change that no longer reproduces the behaviour the
/// twin was generated against — the drift a build-time file comparison cannot see.</item>
/// </list>
///
/// Framework-agnostic, like <see cref="DifferentialCorpus"/>: it returns human-readable problems (empty ==
/// everything agrees) and the consumer asserts in whatever test framework it uses.
/// <code>
/// var problems = MachineConformance.Check(new MyMachine(), irPath, corpusPath);
/// Assert.That(problems, Is.Empty, string.Join("\n", problems));
/// </code>
/// </summary>
public static class MachineConformance
{
    /// <summary>
    /// Run all three checks. <paramref name="irPath"/> and <paramref name="corpusPath"/> are the committed
    /// artifacts, which normally live at a repo path shared with the frontend rather than beside the
    /// assembly. A missing file is reported, not thrown.
    /// </summary>
    public static IReadOnlyList<string> Check(IMachine machine, string irPath, string corpusPath)
    {
        var problems = new List<string>();

        problems.AddRange(CheckIr(machine, irPath));
        problems.AddRange(CheckCorpus(machine, corpusPath));
        problems.AddRange(
            machine
                .SelfCheck()
                .Select(diff => $"self-check: the embedded corpus no longer replays: {diff}")
        );

        return problems;
    }

    /// <summary>The committed IR still equals what the machine exports.</summary>
    public static IReadOnlyList<string> CheckIr(IMachine machine, string irPath)
    {
        if (!File.Exists(irPath))
            return
            [
                $"IR: no committed IR at {irPath} — regenerate it and commit it alongside the machine.",
            ];

        var exported = machine.ExportIr();
        return Same(File.ReadAllText(irPath), exported)
            ? []
            :
            [
                $"IR: the committed {Path.GetFileName(irPath)} no longer matches {machine.Name}.ExportIr(). "
                    + "Regenerate the machine's artifacts and commit them with the source.",
            ];
    }

    /// <summary>The committed corpus still equals the one embedded in this build.</summary>
    public static IReadOnlyList<string> CheckCorpus(IMachine machine, string corpusPath)
    {
        if (machine.Corpus is null)
            return File.Exists(corpusPath)
                ?
                [
                    $"corpus: {machine.Name} ships no embedded corpus, but one is committed at {corpusPath}. "
                        + "Embed it so the deployed engine can self-check.",
                ]
                : [];

        if (!File.Exists(corpusPath))
            return
            [
                $"corpus: {machine.Name} embeds a corpus, but none is committed at {corpusPath}.",
            ];

        return Same(File.ReadAllText(corpusPath), machine.Corpus)
            ? []
            :
            [
                $"corpus: the committed {Path.GetFileName(corpusPath)} differs from the copy embedded in "
                    + $"{machine.Name}. The assembly was built against a different golden — rebuild after regenerating.",
            ];
    }

    /// <summary>
    /// Rewrite the committed IR from the machine. For a consumer's "regenerate" escape hatch; the normal
    /// path is the repo's own regen script, which rewrites the IR and everything downstream of it in order.
    /// </summary>
    public static void WriteIr(IMachine machine, string irPath) =>
        File.WriteAllText(irPath, machine.ExportIr() + "\n");

    // Committed artifacts carry a trailing newline; the in-memory forms do not.
    private static bool Same(string file, string inMemory) =>
        file.TrimEnd('\n', '\r') == inMemory.TrimEnd('\n', '\r');
}
