namespace Trax.Effect.Data.Services.Drain;

/// <summary>
/// Trains whose runs a drain does not wait on, by canonical name. The scheduler registers its own (the manifest
/// manager, the dispatcher, the cleanups): they keep the scheduler's bookkeeping going while it is drained and end in
/// moments, and a drain that waited on them could never be sure it had seen the last.
/// </summary>
/// <param name="Names">The canonical train names.</param>
internal sealed record DrainIgnoredTrains(IReadOnlyList<string> Names);
