namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// The <c>trax.checkpoint</c> table, as a run reads and writes it. Implemented by
/// <c>Trax.Effect.Data</c> over a data context of its own, never the run's effect runner, so a
/// checkpoint saves nothing else the run tracks (effect/0021).
/// </summary>
internal interface ICheckpointRows
{
    /// <summary>Stores one checkpoint a run reached.</summary>
    Task Insert(Models.Checkpoint.Checkpoint row, CancellationToken cancellationToken);

    /// <summary>
    /// The run <paramref name="runId"/> and every run it resumed, following <c>resume_from</c>
    /// back, nearest first, each with the checkpoints it wrote. The rows carry everything but
    /// their state, which is null: a verdict needs none, and a run reads only the states it
    /// restores, with <see cref="States"/>.
    /// </summary>
    Task<IReadOnlyList<ResumedRun>> Lineage(long runId, CancellationToken cancellationToken);

    /// <summary>The stored state of each checkpoint row in <paramref name="rowIds"/> that still exists, by row id.</summary>
    Task<IReadOnlyDictionary<long, string>> States(
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// What a resume of <paramref name="runId"/> checks about the run itself before trusting its
    /// checkpoints, or null when no such run exists.
    /// </summary>
    Task<ResumeSource?> Source(long runId, CancellationToken cancellationToken);

    /// <summary>Deletes the checkpoints of <paramref name="runId"/>, which completed.</summary>
    Task DeleteFor(long runId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the Trax data context of <paramref name="scope"/> holds changes it has not saved or
    /// an open transaction: work a resume would skip without it ever having committed.
    /// </summary>
    bool HasUncommittedWork(IServiceProvider scope);
}
