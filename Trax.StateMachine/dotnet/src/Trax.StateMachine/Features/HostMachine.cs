using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// One machine of a host file. <see cref="SystemKey"/> is null for a machine its users own and for a keyed system
/// machine.
/// </summary>
internal sealed record HostMachine(
    string Id,
    string SchemaHash,
    string? SystemKey,
    AccessRule? Start,
    AccessRule? Read,
    IReadOnlyDictionary<string, TriggerAccess> Triggers,
    IReadOnlyDictionary<string, HostInvoke> Invokes
)
{
    /// <summary>
    /// A keyed system machine's key parts by name (<c>{ "system": { "keyedBy": [...] } }</c>): one instance per key,
    /// which host code starts and moves through <c>IMachineInstances</c>. The host starts none at startup.
    /// </summary>
    internal IReadOnlyList<string>? KeyedBy { get; init; }

    /// <summary>Whether the system owns the machine's instances, at a fixed key or keyed.</summary>
    internal bool IsSystem => SystemKey is not null || KeyedBy is not null;

    /// <summary>The <c>ISnapshotEffect</c> interface each effect-bound trigger runs, by trigger, as a full name.</summary>
    internal IReadOnlyDictionary<string, string> Effects { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The <c>ICustomRule</c> type of each custom rule the IR names, by name, as a full name.</summary>
    internal IReadOnlyDictionary<string, string> Rules { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The <c>ICustomReduction</c> type of each custom reduction the IR names, by name, as a full name.</summary>
    internal IReadOnlyDictionary<string, string> Reductions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The <c>IMachineMigration</c> type of each version step, by the version it steps from.</summary>
    internal IReadOnlyDictionary<int, string> Migrations { get; init; } =
        new Dictionary<int, string>();
}
