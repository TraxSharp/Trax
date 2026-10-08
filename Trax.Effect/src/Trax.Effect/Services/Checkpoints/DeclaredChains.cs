using System.Collections.Concurrent;
using Trax.Core.Monad;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Each train class's declared chain, its hash and whether it declares a checkpoint, kept for the
/// process. The host's startup check reads every registered train's chain and remembers it here,
/// so a run takes its chain from here instead of calling <c>Junctions()</c> a second time. A host
/// that skips the check (or runs trains without a mediator) has a run read its own on first need.
/// </summary>
internal static class DeclaredChains
{
    private static readonly ConcurrentDictionary<Type, Declared?> Chains = new();

    /// <summary>
    /// Remembers <paramref name="train"/>'s chain, read by someone who already had to read it.
    /// </summary>
    public static void Remember(Type train, ChainRecorder chain, Type input, Type output) =>
        Chains.TryAdd(train, Of(train, chain, input, output));

    /// <summary>
    /// <paramref name="train"/>'s chain, read with <paramref name="declare"/> the first time no one
    /// has remembered it, or null when it cannot be read.
    /// </summary>
    public static Declared? For(
        Type train,
        Type input,
        Type output,
        Func<ChainRecorder> declare,
        Action<Exception> unreadable
    ) =>
        Chains.GetOrAdd(
            train,
            _ =>
            {
                try
                {
                    return Of(train, declare(), input, output);
                }
                catch (Exception e)
                {
                    unreadable(e);
                    return null;
                }
            }
        );

    private static Declared Of(Type train, ChainRecorder chain, Type input, Type output) =>
        new(chain, ChainGraph.From(chain, train, input, output).Hash, HasCheckpoint(chain));

    private static bool HasCheckpoint(ChainRecorder chain) =>
        chain
            .Steps.Select((step, i) => (step, i))
            .Any(s =>
                s.step.Kind == ChainStepKind.Checkpoint
                || chain.TracksAt(s.i).Any(t => HasCheckpoint(t.Steps))
            );

    /// <summary>A train class's chain, its hash, and whether it declares a checkpoint anywhere.</summary>
    internal sealed record Declared(ChainRecorder Chain, string Hash, bool Checkpoints);
}
