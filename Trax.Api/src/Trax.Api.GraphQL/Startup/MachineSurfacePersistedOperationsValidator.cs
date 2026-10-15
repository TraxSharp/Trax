using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails the host at startup when it exposes machines through the typed GraphQL surface without persisted operations.
/// </summary>
/// <remarks>
/// A machine's operations are served as persisted documents whose ids carry the machine's IR hash, and the store those
/// documents are registered in is what orders the versions of a machine across the nodes of a rolling deploy. Without
/// it a node cannot tell a row written by a newer version (which ends a the push (<c>&lt;machine&gt;Changed</c>) stream) from one written
/// by an older node mid-deploy (which does not), so it refuses to start rather than guess. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </remarks>
internal sealed class MachineSurfacePersistedOperationsValidator(
    IServiceProviderIsService isService,
    IReadOnlyList<string> exposed
) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        if (exposed.Count == 0 || isService.IsService(typeof(IMachineIrOrder)))
            return Task.CompletedTask;

        throw new InvalidOperationException(
            $"AddStateMachines exposes {string.Join(", ", exposed.Select(m => $"'{m}'"))} through the typed GraphQL "
                + "surface, but persisted operations are not configured. Each exposed machine is served as persisted "
                + "documents that carry its version, and the node orders a machine's versions across a rolling "
                + "deploy through them; without them it cannot tell a newer version's write from an older node's.\n"
                + "Add UsePersistedOperations to AddTraxGraphQL: "
                + "AddTraxGraphQL(graphql => graphql.UsePersistedOperations(po => po.SingleNode())) on one node, or "
                + "po.UseRabbitMqInvalidation(connectionString) when more than one node serves the endpoint."
        );
    }
}
