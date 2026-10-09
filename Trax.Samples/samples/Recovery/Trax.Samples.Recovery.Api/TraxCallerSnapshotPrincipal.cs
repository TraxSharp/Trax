using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.Recovery.Api;

/// <summary>
/// Maps the authenticated caller to the key a topic map draft is stored under, so each demo key has
/// drafts of its own and nobody loads another's. Null for an anonymous request, which every
/// <c>stateMachine</c> mutation then refuses.
/// </summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
