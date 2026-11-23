using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

/// <summary>
/// Reads the caller from the injected <see cref="TraxPrincipal"/>. Its <c>Id</c> is qualified by
/// the scheme that authenticated the request: <c>TraxApiKey:alice</c> for Alice's key,
/// <c>TraxJwt:alice</c> for her token.
/// </summary>
public class DescribeCallerJunction(TraxPrincipal caller) : Junction<WhoAmIInput, WhoAmIOutput>
{
    public override Task<WhoAmIOutput> Run(WhoAmIInput input) =>
        Task.FromResult(
            new WhoAmIOutput(caller.Id, caller.DisplayName, caller.Roles, caller.PrincipalType)
        );
}
