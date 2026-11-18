using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One state machine the host registers, as <c>operations.machines</c> serves it: its name, version and graph,
/// whether or not it has any instances.
/// </summary>
/// <remarks>
/// The graph is read from the machine's IR by the same reading <c>trax machine show</c> draws it from. It carries
/// only what the machine declares: no instance, no context, no input.
/// </remarks>
/// <param name="Name">The machine's id.</param>
/// <param name="Version">The definition version the machine declares.</param>
/// <param name="SystemOwned">True when only the system holds its instances.</param>
public record MachineDefinition(string Name, int Version, bool SystemOwned)
{
    /// <summary>The hash of the machine's IR; null for a machine with no exportable IR.</summary>
    public string? SchemaHash { get; init; }

    /// <summary>The state an instance starts in; null when the machine has no exportable IR.</summary>
    public string? InitialState { get; init; }

    /// <summary>Every state, in the order the IR lists them.</summary>
    public IReadOnlyList<MachineDefinitionState> States { get; init; } = [];

    /// <summary>Every transition: commands, then timers, then outcomes in the order they are tried.</summary>
    public IReadOnlyList<MachineDefinitionTransition> Transitions { get; init; } = [];

    /// <summary>Every timed state's timer.</summary>
    public IReadOnlyList<MachineDefinitionTimer> Timers { get; init; } = [];

    /// <summary>Why the machine has no graph (it was declared with C# delegates); null when it has one.</summary>
    public string? GraphUnavailableReason { get; init; }

    internal static MachineDefinition From(MachineDescription d) =>
        new(d.Name, d.Version, d.SystemOwned)
        {
            SchemaHash = d.SchemaHash,
            InitialState = d.InitialState,
            States = d
                .States.Select(s => new MachineDefinitionState(s.Name)
                {
                    IsInitial = s.IsInitial,
                    IsFinal = s.IsFinal,
                    IsCommitted = s.IsCommitted,
                    InvokesTrain = s.InvokesTrain,
                    SpawnsMachine = s.SpawnsMachine,
                })
                .ToList(),
            Transitions = d
                .Transitions.Select(t => new MachineDefinitionTransition(
                    t.From,
                    t.Command,
                    t.To,
                    t.Kind
                )
                {
                    Guard = t.Guard,
                    Effect = t.Effect,
                    After = t.After,
                })
                .ToList(),
            Timers = d
                .Timers.Select(t => new MachineDefinitionTimer(t.State, t.Command, t.Target)
                {
                    After = t.After,
                    Field = t.Field,
                    Min = t.Min,
                    Max = t.Max,
                })
                .ToList(),
            GraphUnavailableReason = d.GraphUnavailableReason,
        };
}
