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
/// What a write that does not say does to a row's timer, by machine, state and context, from the registered machines'
/// timers (see <see cref="EfSnapshotStore.Timers"/>). Null for a name no registered machine has, and for a duration the
/// context cannot give.
/// </summary>
internal sealed class MachineTimerLookup(IEnumerable<IMachine> machines)
{
    private readonly Lazy<IReadOnlyDictionary<string, IMachineInternals>> _byName = new(() =>
        machines
            .OfType<IMachineInternals>()
            .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    /// <summary>The timer a write in <paramref name="state"/> keeps from entry, none, or null when it cannot tell.</summary>
    public SnapshotTimer? Of(string machine, string state, JsonObject context) =>
        _byName.Value.TryGetValue(machine, out var found)
            ? found.TimerFor(state, context, restart: false).Timer
            : null;
}
