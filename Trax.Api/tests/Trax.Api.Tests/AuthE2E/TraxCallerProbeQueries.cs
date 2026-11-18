using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.Auth;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Reads the current <see cref="TraxCaller"/> on a query. Used to assert what
/// an HTTP request's server-side scope actually sees. NOT gated with
/// <c>[TraxAuthorize]</c> so the anonymous-attack assertions can run.
/// </summary>
[ExtendObjectType("RootQuery")]
public sealed class TraxCallerProbeQueries
{
    [TraxAllowAnonymous]
    public TraxCallerProbeResult WhoAmI([Service] TraxCaller caller) =>
        new(
            IsAuthenticated: caller.IsAuthenticated,
            IsTrusted: caller.IsTrusted,
            PrincipalId: caller.Principal?.Id,
            PrincipalRoles: caller.Principal?.Roles ?? Array.Empty<string>()
        );
}
