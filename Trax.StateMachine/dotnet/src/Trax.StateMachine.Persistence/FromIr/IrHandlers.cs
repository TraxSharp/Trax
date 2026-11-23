using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// The C# a host entry binds to what a machine's IR names (ADR <c>Trax.Docs/adr/0058</c>): the <c>ISnapshotEffect</c>
/// interface each effect-bound trigger runs, and the handler of each custom rule, custom reduction and version step.
/// </summary>
internal sealed record IrHandlers(
    IReadOnlyDictionary<string, Type> Effects,
    IReadOnlyDictionary<string, ICustomRule> Rules,
    IReadOnlyDictionary<string, ICustomReduction> Reductions,
    IReadOnlyDictionary<int, IMachineMigration> Migrations
)
{
    internal static readonly IrHandlers None = new(
        new Dictionary<string, Type>(StringComparer.Ordinal),
        new Dictionary<string, ICustomRule>(StringComparer.Ordinal),
        new Dictionary<string, ICustomReduction>(StringComparer.Ordinal),
        new Dictionary<int, IMachineMigration>()
    );
}
