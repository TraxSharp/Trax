using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SnapshotNotices;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The IR hash (<see cref="IMachine.SchemaHash"/>) of each registered machine by name, which the store writes to a
/// row's <c>ir_hash</c>. Null for a machine with no exportable IR, and for a name no registered machine has.
/// </summary>
internal sealed class MachineIrHashes(IEnumerable<IMachine> machines)
{
    private readonly Lazy<IReadOnlyDictionary<string, IMachine>> _byName = new(() =>
        machines
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    /// <summary>The IR hash of the machine named <paramref name="machine"/>, or null.</summary>
    public string? Of(string machine) =>
        _byName.Value.TryGetValue(machine, out var found) ? found.SchemaHash : null;
}
