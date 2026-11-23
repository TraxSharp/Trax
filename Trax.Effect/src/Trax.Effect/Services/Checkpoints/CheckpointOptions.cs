namespace Trax.Effect.Services.Checkpoints;

/// <summary>How large a checkpoint's state may be.</summary>
internal sealed class CheckpointOptions
{
    /// <summary>
    /// The largest state a checkpoint stores, in bytes of JSON: 1 MiB unless the mediator sets it
    /// to the cap a requeue's stored input uses. A larger one fails the step, classified permanent.
    /// </summary>
    public int MaxStateBytes { get; set; } = 1024 * 1024;
}
