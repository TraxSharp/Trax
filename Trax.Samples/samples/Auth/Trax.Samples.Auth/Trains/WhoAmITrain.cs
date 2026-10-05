using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

/// <summary>Empty on purpose: a query train needs its own input type, and the field then takes no argument.</summary>
public record WhoAmIInput;

public record WhoAmIOutput(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? PrincipalType
);

public interface IWhoAmITrain : IServiceTrain<WhoAmIInput, WhoAmIOutput>;

/// <summary>
/// Any signed-in caller, whatever the scheme or role. A bare <c>[TraxAuthorize]</c> requires an
/// authenticated caller and nothing more.
/// </summary>
[TraxQuery]
[TraxAuthorize]
public class WhoAmITrain : ServiceTrain<WhoAmIInput, WhoAmIOutput>, IWhoAmITrain
{
    protected override Task<Either<Exception, WhoAmIOutput>> Junctions() =>
        Chain<DescribeCallerJunction>().Resolve();
}

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
