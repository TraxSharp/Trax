using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Scheduler.Services.Operations;

// The machines the host registers, described from their IR: the one path the console's Machines page and the API's
// operations.machines share (central docs/0022).
public partial class OperationsService
{
    /// <inheritdoc />
    /// <remarks>
    /// Read from the machines registered in the container, not from instance rows, so a machine with no instance is
    /// listed. Each machine is a singleton whose definition does not change while the host runs, so its description
    /// is built once and kept for the machine's lifetime.
    /// </remarks>
    public Task<IReadOnlyList<MachineDescription>> GetMachinesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var registered = RegisteredMachines
            .Distinct(_services?.GetServices<IMachine>() ?? [])
            .ToList();

        IReadOnlyList<MachineDescription> described = registered
            .DistinctBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => Descriptions.GetValue(m, machine => Describe(machine, registered)))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(described);
    }

    private static readonly ConditionalWeakTable<IMachine, MachineDescription> Descriptions = new();

    internal static MachineDescription Describe(
        IMachine machine,
        IReadOnlyList<IMachine> registered
    )
    {
        var internals = machine as IMachineInternals;
        string ir;
        try
        {
            ir = machine.ExportIr();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // A machine declared with delegates has no IR; it is still listed, with why it has no graph.
            return new MachineDescription(
                machine.Name,
                internals?.Version ?? 0,
                internals?.SystemOwned ?? false
            )
            {
                GraphUnavailableReason = ex.Message,
            };
        }

        var graph = MachineGraph.Parse(ir);
        var invokes = (internals?.InvokedTrains ?? []).ToDictionary(
            i => i.State,
            i => i.TrainType.FullName ?? i.TrainType.Name,
            StringComparer.Ordinal
        );
        var spawns = (internals?.Spawns ?? []).ToDictionary(
            s => s.State,
            s =>
                registered.FirstOrDefault(s.ChildMachine.IsInstanceOfType)?.Name
                ?? s.ChildMachine.FullName
                ?? s.ChildMachine.Name,
            StringComparer.Ordinal
        );
        var left = graph.Transitions.Select(t => t.From).ToHashSet(StringComparer.Ordinal);

        return new MachineDescription(
            machine.Name,
            internals?.Version ?? (int.TryParse(graph.Version, out var v) ? v : 0),
            internals?.SystemOwned ?? false
        )
        {
            SchemaHash = machine.SchemaHash,
            InitialState = graph.Initial,
            States = graph
                .States.Select(s => new MachineStateDescription(s)
                {
                    IsInitial = s == graph.Initial,
                    IsFinal = !left.Contains(s),
                    IsCommitted = graph.Committed.Contains(s),
                    InvokesTrain = spawns.ContainsKey(s) ? null : invokes.GetValueOrDefault(s),
                    SpawnsMachine = spawns.GetValueOrDefault(s),
                })
                .ToList(),
            Transitions = graph
                .Transitions.Select(t => new MachineTransitionDescription(
                    t.From,
                    t.Trigger,
                    t.To,
                    t.Kind switch
                    {
                        MachineEdgeKind.Timer => MachineTransitionKind.Timer,
                        MachineEdgeKind.Outcome => MachineTransitionKind.Outcome,
                        _ => MachineTransitionKind.Command,
                    }
                )
                {
                    Guard = t.Guard,
                    Effect = t.Effect,
                    After = t.Timer,
                })
                .ToList(),
            Timers = graph
                .Timers.Select(t => new MachineTimerDescription(t.State, t.Trigger, t.Target)
                {
                    After = t.After,
                    Field = t.Field,
                    Min = t.Min,
                    Max = t.Max,
                })
                .ToList(),
        };
    }
}
