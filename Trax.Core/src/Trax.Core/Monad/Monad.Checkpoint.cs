using System.Diagnostics.CodeAnalysis;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>The chain steps the train methods of the same name call.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/train-methods/checkpoint">SDK reference</seealso>
public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// The node this monad is skipping to, while a resumed run has not reached it yet, and whether
    /// it skips that node too. Null once reached, and on a run that is not resuming.
    /// </summary>
    internal (string Target, bool Inclusive)? Resuming { get; set; }

    /// <summary>
    /// Stores the <typeparamref name="TState"/> in Memory, so a later run of the same input can
    /// resume here instead of running every step before it again.
    /// </summary>
    /// <remarks>
    /// <para>Only <typeparamref name="TState"/> is stored, with the tracks routing steps took
    /// before it; nothing else in Memory is. A resumed run gets it back, beside its input and
    /// services, and skips every step before this one. Whether a run can resume at a later step
    /// depends on what those steps need, and is checked before it runs.</para>
    /// <para>The state is stored as JSON and read back, so it must survive that: a sealed type
    /// whose members are data, which the chain refuses otherwise when it is read. A run with no
    /// store (a plain <c>Train</c>) takes no checkpoint.</para>
    /// </remarks>
    public MonadTask<TInput, TReturn> Checkpoint<TState>() =>
        Recorder is not null ? RecordCheckpoint<TState>() : new(CheckpointAsync<TState>());

    private async Task<Monad<TInput, TReturn>> CheckpointAsync<TState>()
    {
        if (Exception is not null)
            return this;

        var key = ChainNodeScope.CheckpointKey(typeof(TState));

        // A resumed run restored this state before its first step, so it only counts the node.
        if (SkipStep(key))
            return this;

        CancellationToken.ThrowIfCancellationRequested();

        var nodeId = Nodes.Next(key);
        ChainGraph.Enter(nodeId, BranchPath);
        var step = $"Checkpoint<{typeof(TState).ReadableName()}>";

        // Memory only: a state the container supplies is not something the run produced.
        if (!Memory.TryGetValue(typeof(TState), out var state) || state is null)
            return Refuse(
                step,
                $"{step} (train '{Train.GetType().ReadableName()}') found no "
                    + $"'{typeof(TState).ReadableName()}' in Memory. Chain the junction that "
                    + "produces it before the checkpoint."
            );

        if (Optional<ICheckpointStore>() is not { } store)
            return this;

        var tracks = Memory
            .Where(m =>
                m.Key.IsGenericType && m.Key.GetGenericTypeDefinition() == typeof(TrackTaken<>)
            )
            .Select(m => m.Value)
            .ToList();

        try
        {
            await store
                .Write(
                    new CheckpointTaken(
                        Train,
                        Train.ExternalId,
                        nodeId,
                        BranchPath,
                        typeof(TState),
                        state,
                        tracks,
                        Memory.GetValueOrDefault(typeof(IServiceProvider)) as IServiceProvider
                    ),
                    CancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return Failed(e, step);
        }

        return this;
    }

    private MonadTask<TInput, TReturn> RecordCheckpoint<TState>()
    {
        var recorder = Recorder.NotNull();
        var step = $"Checkpoint<{typeof(TState).ReadableName()}>";

        if (recorder.AfterShortCircuit)
            recorder.RefuseStep(
                ChainStepKind.Checkpoint,
                junction: null,
                $"{step} comes after a ShortCircuit on the same path. A short circuit's value is "
                    + "kept outside Memory, so a run resumed here would lose it. Checkpoint before "
                    + "the ShortCircuit, or after a step that does not need it."
            );

        foreach (var problem in CheckpointState.Problems(typeof(TState)))
            recorder.RefuseStep(ChainStepKind.Checkpoint, junction: null, $"{step}: {problem}");

        recorder.Record(ChainStepKind.Checkpoint, junction: null, typeof(TState), tOut: null);
        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }

    #region Resuming

    /// <summary>
    /// Whether the step about to be numbered under <paramref name="key"/> is skipped because a
    /// resumed run has not reached its resume point. A skipped step's node is counted, so every
    /// id after it is the one the original run had. Reaching the point ends the skipping, before
    /// the point when it runs, after it when it is a restored checkpoint.
    /// </summary>
    private bool SkipStep(string key)
    {
        if (Resuming is not { } resume)
            return false;

        var id = Nodes.Peek(key);

        if (!resume.Inclusive && string.Equals(id, resume.Target, StringComparison.Ordinal))
        {
            Resuming = null;
            return false;
        }

        Nodes.Next(key);

        if (resume.Inclusive && string.Equals(id, resume.Target, StringComparison.Ordinal))
            Resuming = null;

        return true;
    }

    /// <summary>
    /// What a skipping run does at a routing step: runs it when it is the resume point, enters the
    /// one track the resume point is inside, or counts it and moves on. Null when the step runs.
    /// </summary>
    private async Task<Monad<TInput, TReturn>?> SkipRouting<TKey>(
        ChainStepKind kind,
        string step,
        TrackSet<TInput, TReturn> tracks
    )
    {
        if (Resuming is not { } resume)
            return null;

        var key = ChainNodeScope.RoutingKey(kind, typeof(TKey));
        var id = Nodes.Peek(key);

        if (!resume.Inclusive && string.Equals(id, resume.Target, StringComparison.Ordinal))
        {
            Resuming = null;
            return null;
        }

        Nodes.Next(key);

        if (!resume.Target.StartsWith(id + "/", StringComparison.Ordinal))
            return this;

        // The track is named in the resume point's id, so the run goes down the one it is in.
        var name = resume.Target[(id.Length + 1)..].Split('/')[0];
        var taken =
            tracks.Tracks.Find(t => string.Equals(t.Name, name, StringComparison.Ordinal))
            ?? (
                string.Equals(tracks.Fallback?.Name, name, StringComparison.Ordinal)
                    ? tracks.Fallback
                    : null
            );

        if (taken is null)
            return Refuse(
                step,
                $"{step} (train '{Train.GetType().ReadableName()}') has no track '{name}' to "
                    + $"resume in, so the resume point '{resume.Target}' does not exist."
            );

        var outer = Nodes;
        Nodes = outer.Track(id, taken.Name);

        try
        {
            return await taken
                .Body(new MonadTask<TInput, TReturn>(Task.FromResult(this)))
                .ConfigureAwait(false);
        }
        finally
        {
            Nodes = outer;
        }
    }

    /// <summary>
    /// Counts a <c>Decide</c>'s questions without asking them, as a skipping run does, so each
    /// later asking of the same question keeps the occurrence it had in the original run. True
    /// when they were skipped; false when the resume point is one of them and they are asked.
    /// </summary>
    private bool SkipQuestions(IReadOnlyList<QuestionSpec> specs)
    {
        if (Resuming is not { } resume)
            return false;

        var keys = specs.Select(s => ChainNodeScope.DecideKey(s.DecisionType)).ToList();

        if (
            !resume.Inclusive
            && Nodes.PeekMany(keys).Contains(resume.Target, StringComparer.Ordinal)
        )
        {
            Resuming = null;
            return false;
        }

        foreach (var key in keys)
            Nodes.Next(key);

        foreach (var spec in specs)
        {
            _askings[spec.Key] = _askings.GetValueOrDefault(spec.Key) + 1;
            _askedAbout.TryAdd(spec.Key, spec.On);
        }

        return true;
    }

    /// <summary>
    /// Puts a restored checkpoint's state and routes into Memory, as they were when it was taken.
    /// </summary>
    internal void Restore(RestoredCheckpoint checkpoint)
    {
        Memory[checkpoint.StateType] = checkpoint.State;

        foreach (var track in checkpoint.Tracks)
            Memory[track.GetType()] = track;
    }

    /// <summary>
    /// Fails a resumed run whose chain ended without reaching its resume point: the point is not
    /// on the path this run took, so nothing after it ran.
    /// </summary>
    private void FailUnreachedResumePoint()
    {
        if (Resuming is not { } resume || Exception is not null)
            return;

        Refuse(
            "Resume",
            $"The resumed run of '{Train.GetType().ReadableName()}' ended without reaching its "
                + $"resume point '{resume.Target}', which is not on the path this run took."
        );
    }

    #endregion
}
