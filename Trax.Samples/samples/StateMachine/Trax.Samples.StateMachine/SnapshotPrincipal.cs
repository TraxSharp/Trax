using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

/// <summary>
/// Maps Trax's authenticated caller to the snapshot user key (drafts are scoped per user), or null when the
/// request is anonymous. This is the one line a host writes to connect its auth to snapshot user-scoping.
/// </summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
