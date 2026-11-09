using Trax.Api.Auth;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Bakery.Api;

/// <summary>
/// Maps the signed-in person to the key their orders and shifts are stored under, so each person has instances of their
/// own. Null for an anonymous request, which every machine operation then refuses.
/// </summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
