using Trax.Api.Auth;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Api;

/// <summary>
/// Maps the authenticated caller to the key an assignment is stored under, so each agent owns its own assignments and
/// no agent reads or moves another's. Null for an anonymous request, which every machine operation then refuses.
/// </summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
