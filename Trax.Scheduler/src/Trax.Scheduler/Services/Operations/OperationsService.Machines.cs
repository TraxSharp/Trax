using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence;

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

    /// <inheritdoc />
    /// <remarks>
    /// Drawn from the host file the host serves and the IRs of its machines, so it shows what runs, not what the
    /// features say today. The lanes are the machine's own and each machine a policy or a view on the whole board joins it
    /// to; an actor joins none.
    /// </remarks>
    public Task<MachineBoardDescription?> GetMachineBoardAsync(string machine, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            _services is not { } services
            || services.GetService<HostFile>() is not { } host
            || !host.Machines.ContainsKey(machine)
        )
            return Task.FromResult<MachineBoardDescription?>(null);
        var irs = RegisteredMachines
            .Distinct(services.GetServices<IMachine>())
            .Where(m => host.Machines.ContainsKey(m.Name))
            .DistinctBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(
                m => m.Name,
                m => JsonNode.Parse(m.ExportIr())!.AsObject(),
                StringComparer.Ordinal
            );
        var drawn = Board.Draw(irs, host);
        // An actor sticky is one per rule on the whole board, in the lane of the first machine that drew it, so two
        // machines whose rules admit the same callers share one. It joins nothing: a policy or a view does.
        var byId = drawn.Stickies.ToDictionary(s => s.Id, StringComparer.Ordinal);
        bool IsActor(string id) => byId[id].Kind == Board.Kind.Actor;
        var lane = drawn
            .Stickies.Where(s => s.Machine == machine && s.Kind != Board.Kind.Actor)
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);
        var joined = drawn
            .Links.Where(l => !IsActor(l.From) && !IsActor(l.To))
            .Where(l => lane.Contains(l.From) || lane.Contains(l.To))
            .SelectMany(l => new[] { l.From, l.To })
            .Select(id => byId[id].Machine)
            .Append(machine)
            .ToHashSet(StringComparer.Ordinal);
        var drawnHere = drawn
            .Stickies.Where(s => s.Kind != Board.Kind.Actor && joined.Contains(s.Machine))
            .ToList();
        var here = drawnHere.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        // An actor is shown when an arrow ties it to a sticky on this board: in its own lane if it is tied to that
        // machine, otherwise in the first lane it is tied to, the asked-for machine's first.
        var lanes = joined
            .OrderBy(m => m == machine ? 0 : 1)
            .ThenBy(m => m, StringComparer.Ordinal)
            .ToList();
        var actors = drawn
            .Stickies.Where(s => s.Kind == Board.Kind.Actor)
            .Select(actor =>
            {
                var tied = drawn
                    .Links.Where(l => l.From == actor.Id || l.To == actor.Id)
                    .Select(l => l.From == actor.Id ? l.To : l.From)
                    .Where(here.Contains)
                    .Select(id => byId[id].Machine)
                    .ToHashSet(StringComparer.Ordinal);
                return tied.Count == 0
                    ? null
                    : actor with
                    {
                        Machine = tied.Contains(actor.Machine)
                            ? actor.Machine
                            : lanes.First(tied.Contains),
                    };
            })
            .OfType<Board.Sticky>();
        var stickies = drawnHere
            .Concat(actors)
            .OrderBy(s => s.Machine, StringComparer.Ordinal)
            .ThenBy(s => s.Kind)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        var shown = stickies.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult<MachineBoardDescription?>(
            new MachineBoardDescription(
                machine,
                lanes,
                stickies
                    .Select(s => new MachineStickyDescription(
                        s.Id,
                        s.Kind.ToString().ToLowerInvariant(),
                        s.Machine,
                        s.Label,
                        s.Scenarios.Select(p => new MachineScenarioDescription(
                                p.File,
                                p.Line,
                                p.Scenario
                            ))
                            .ToList()
                    ))
                    .ToList(),
                drawn
                    .Links.Where(l => shown.Contains(l.From) && shown.Contains(l.To))
                    .Select(l => new MachineBoardLinkDescription(l.From, l.To))
                    .ToList()
            )
        );
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
