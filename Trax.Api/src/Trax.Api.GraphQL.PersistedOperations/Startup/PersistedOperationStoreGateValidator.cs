using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.Startup;

/// <summary>
/// Refuses to start a process whose <c>AddPersistedOperationStore</c> declares a management gate
/// other than the one <c>UsePersistedOperations</c> declares.
/// </summary>
/// <remarks>
/// A process has one set of persisted-operation options, and <c>UsePersistedOperations</c>' are
/// the ones in force whichever call comes first: the GraphQL fields carry its gate, and the
/// dashboard's pages, enforcement and the inline carve-out read the same options. A store that
/// declares a different gate would otherwise be ignored without a word, so its declaration is
/// refused instead. A store that declares no gate takes GraphQL's.
/// </remarks>
internal sealed class PersistedOperationStoreGateValidator(
    PersistedOperationsOptions options,
    IEnumerable<PersistedOperationStoreDeclaration> stores
) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        var graphql = Describe(options.ManagementGate);
        foreach (var store in stores)
        {
            if (store.ManagementGate.Count == 0)
                continue;

            var declared = Describe(store.ManagementGate);
            if (declared == graphql)
                continue;

            throw new InvalidOperationException(
                $"AddPersistedOperationStore declares the management gate {declared} and "
                    + $"UsePersistedOperations declares {graphql}, but one process has one gate, "
                    + "UsePersistedOperations', so declare it there and remove GateManagement "
                    + "from AddPersistedOperationStore."
            );
        }
        return Task.CompletedTask;
    }

    private static string Describe(IReadOnlyList<TraxAuthorizeAttribute> gate)
    {
        if (gate.Count == 0)
            return "(none)";

        AuthorizeDirectives.ExtractRules(gate, out var policies, out var roleSets);
        var requirements = policies
            .Select(p => $"policy '{p}'")
            .Concat(roleSets.Select(r => $"roles '{string.Join(",", r)}'"))
            .Order(StringComparer.Ordinal)
            .ToList();
        return requirements.Count == 0
            ? "(any authenticated user)"
            : "(" + string.Join(" and ", requirements) + ")";
    }
}
