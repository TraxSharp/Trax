using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Binds the state-machine's <see cref="ISnapshotPrincipal"/> over Trax's own <see cref="TraxCaller"/>: the
/// user key is the authenticated principal's id, or null when the request is anonymous. This is the one
/// line a host writes to map its auth onto snapshot user-scoping. Scoped, so it reads the current request.
/// </summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
