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

/// <summary>One run in a resume lineage, with the checkpoints it wrote.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="ResumeFrom">The run it resumed, or null.</param>
/// <param name="ResumeAt">The node it resumed at, or null for after its source's latest checkpoint.</param>
/// <param name="Rows">The checkpoints it wrote.</param>
internal sealed record ResumedRun(
    long Id,
    long? ResumeFrom,
    string? ResumeAt,
    IReadOnlyList<Models.Checkpoint.Checkpoint> Rows
);

/// <summary>The run a resume names, as the resumed run checks it before restoring anything.</summary>
/// <param name="Name">Its train's name.</param>
/// <param name="Input">Its stored input, or null.</param>
/// <param name="State">How it ended, or that it has not.</param>
/// <param name="ResumedToCompletion">True when a run that resumed it already completed.</param>
internal sealed record ResumeSource(
    string Name,
    string? Input,
    Enums.TrainState State,
    bool ResumedToCompletion
);

/// <summary>How large a checkpoint's state may be.</summary>
public sealed class CheckpointOptions
{
    /// <summary>
    /// The largest state a checkpoint stores, in bytes of JSON: 1 MiB unless the mediator sets it
    /// to the cap a requeue's stored input uses. A larger one fails the step, classified permanent.
    /// </summary>
    public int MaxStateBytes { get; set; } = 1024 * 1024;
}
