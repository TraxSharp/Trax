using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// Stores the steps junction events report, so a run's timeline can be read back. Implemented by
/// Trax.Effect.Data's junction run writer, which <c>AddJunctionEvents</c> registers.
/// </summary>
internal interface IJunctionRunSink
{
    /// <summary>
    /// Takes one step to store against <paramref name="metadataId"/> and returns without waiting for
    /// the write. Must not throw for a write that fails or cannot be queued; it logs instead.
    /// </summary>
    void Write(long metadataId, JunctionEventPayload step);
}
