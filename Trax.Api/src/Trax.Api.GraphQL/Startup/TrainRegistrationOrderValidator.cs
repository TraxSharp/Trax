using Trax.Api.Auth;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Refuses to start a host with a <c>[TraxQuery]</c>, <c>[TraxMutation]</c> or
/// <c>[TraxBroadcast]</c> train registered after <c>AddTraxGraphQL()</c>.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> reads the trains registered so far: it checks each one's
/// authorization posture and name, and decides from them which root types the schema gets
/// (docs/adr/0002-reading-the-service-collection-is-order-dependent.md). A train registered later
/// skips those checks, and its field is missing or the schema does not build, with nothing naming
/// the call order as the cause. This compares the trains the finished container exposes with the
/// ones <c>AddTraxGraphQL()</c> saw, and names each one it missed.
/// </remarks>
internal sealed class TrainRegistrationOrderValidator(
    IReadOnlySet<Type> seenByAddTraxGraphQL,
    ITrainDiscoveryService discovery
) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        var missed = discovery
            .DiscoverTrains()
            .Where(r => r.IsQuery || r.IsMutation || r.IsBroadcastEnabled)
            .Where(r => !seenByAddTraxGraphQL.Contains(r.ServiceType))
            .Select(r => r.ServiceType.FullName)
            .Distinct()
            .ToList();

        if (missed.Count > 0)
            throw new InvalidOperationException(
                "These trains are exposed to GraphQL but were registered after AddTraxGraphQL(), "
                    + "which reads the trains registered before it: "
                    + string.Join(", ", missed)
                    + ". Register them before AddTraxGraphQL(), for example inside AddTrax(...)."
            );

        return Task.CompletedTask;
    }
}
