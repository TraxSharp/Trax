using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Auth;

// A machine instance belongs to the signed-in caller, by their qualified id.
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
