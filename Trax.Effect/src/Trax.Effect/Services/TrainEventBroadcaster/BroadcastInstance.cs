namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// The identity of one running host on the broadcast transport. <c>UseBroadcaster()</c> registers
/// one per service provider, so every replica of an app, and every host started in one process,
/// gets its own. The publishers stamp it on each message as
/// <see cref="TrainLifecycleEventMessage.InstanceId"/>, and <see cref="TrainEventReceiverService"/>
/// drops a message only when it carries this host's id.
/// </summary>
internal sealed class BroadcastInstance
{
    /// <summary>
    /// The instance used where nothing is registered, such as a hook constructed directly.
    /// </summary>
    internal static BroadcastInstance Unregistered { get; } = new();

    /// <summary>A fresh GUID, fixed for the life of this host.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
