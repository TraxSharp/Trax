using Trax.Api.Auth;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Refuses to start a host whose schema has no <c>views</c> root while it registers views of machines served from
/// features.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> decides whether the schema gets the <c>views</c> root from whether the views were
/// registered before it (docs/adr/0002-reading-the-service-collection-is-order-dependent.md). Views registered after it,
/// or through a factory, are seen only once the container is built, and would be silently unserved; this names them
/// instead.
/// </remarks>
internal sealed class ViewRegistrationOrderValidator(MachineViewCatalog? catalog) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        if (catalog is { Views.Count: > 0 })
            throw new InvalidOperationException(
                "These views of machines served from features are registered, but AddTraxGraphQL() saw none of them, so "
                    + "the schema has no views root to serve them under: "
                    + string.Join(
                        ", ",
                        catalog.Views.Keys.Order(StringComparer.Ordinal).Select(v => $"'{v}'")
                    )
                    + ". Register the machines directory before AddTraxGraphQL()."
            );
        return Task.CompletedTask;
    }
}
