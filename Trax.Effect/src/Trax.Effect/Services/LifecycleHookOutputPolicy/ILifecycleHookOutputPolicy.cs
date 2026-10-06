namespace Trax.Effect.Services.LifecycleHookOutputPolicy;

/// <summary>
/// Decides the copy of a completed train's output that lifecycle hooks receive in
/// <c>Metadata.Output</c> when no stored copy was written.
/// </summary>
/// <remarks>
/// When the parameter effect wrote the stored copy, hooks receive that copy, already bounded by
/// <c>MaxParameterBytes</c>. Otherwise the train serializes a copy for the hooks, and this policy
/// says whether to and under what ceiling. The broadcaster, GraphQL and SignalR hooks publish that
/// copy to other processes and every subscriber, so it is bounded like any other serialized
/// parameter. <c>SaveTrainParameters</c> replaces the default with one that follows its own
/// configuration.
/// </remarks>
public interface ILifecycleHookOutputPolicy
{
    /// <summary>
    /// The largest serialized copy, in UTF-8 bytes, of the output of the train named
    /// <paramref name="trainName"/> to build for lifecycle hooks, or <c>null</c> to build none.
    /// </summary>
    /// <remarks>
    /// A copy that would cross the ceiling is replaced by <c>{"_truncated": true, "_maxBytes": N}</c>,
    /// as a stored parameter is. <c>null</c> means the output was deliberately left unserialized,
    /// and the hooks see <c>Metadata.Output</c> as <c>null</c>.
    /// </remarks>
    int? MaxCopyBytes(string trainName);
}
