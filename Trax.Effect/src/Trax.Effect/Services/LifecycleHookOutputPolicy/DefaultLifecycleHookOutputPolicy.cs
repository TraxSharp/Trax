namespace Trax.Effect.Services.LifecycleHookOutputPolicy;

/// <summary>
/// The policy for a host without <c>SaveTrainParameters</c>: every completed train's output is
/// serialized for the lifecycle hooks, up to <see cref="DefaultMaxCopyBytes"/>.
/// </summary>
public sealed class DefaultLifecycleHookOutputPolicy : ILifecycleHookOutputPolicy
{
    /// <summary>
    /// The ceiling on the hook copy when nothing configures one: 1 MiB.
    /// </summary>
    public const int DefaultMaxCopyBytes = 1024 * 1024;

    /// <inheritdoc />
    public int? MaxCopyBytes(string trainName) => DefaultMaxCopyBytes;
}
