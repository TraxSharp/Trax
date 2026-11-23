namespace Trax.Effect.Services.Checkpoints;

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
