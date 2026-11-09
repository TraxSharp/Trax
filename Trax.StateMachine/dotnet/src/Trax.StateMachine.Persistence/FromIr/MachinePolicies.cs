using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// The policies of a host's machines served from features (ADR <c>Trax.Docs/adr/0058</c>), by the identity a fire row
/// names them with, and by the machine and state whose entry sets them off. The store reads <see cref="Entered"/> in a
/// transition's transaction to queue each fire it owes; the runner reads <see cref="ById"/> to fire it.
/// </summary>
internal sealed class MachinePolicies
{
    internal MachinePolicies(IEnumerable<HostPolicy> policies)
    {
        ById = policies.ToDictionary(Id, p => p, StringComparer.Ordinal);
        _entered = ById.GroupBy(p => (p.Value.When, p.Value.Enters))
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<string>)
                        g.Select(p => p.Key).Order(StringComparer.Ordinal).ToList()
            );
    }

    private readonly Dictionary<(string Machine, string State), IReadOnlyList<string>> _entered;

    internal IReadOnlyDictionary<string, HostPolicy> ById { get; }

    /// <summary>The policies <paramref name="machine"/> entering <paramref name="state"/> sets off; empty for none.</summary>
    internal IReadOnlyList<string> Entered(string machine, string state) =>
        _entered.GetValueOrDefault((machine, state)) ?? [];

    /// <summary>A policy's identity: what it waits for and what it fires, <c>shop.Open&gt;order.Bake</c>.</summary>
    internal static string Id(HostPolicy policy) => policy.Id;
}
