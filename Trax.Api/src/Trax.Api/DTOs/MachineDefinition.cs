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

/// <summary>One state of a <see cref="MachineDefinition"/>.</summary>
/// <param name="Name">The state's name.</param>
public record MachineDefinitionState(string Name)
{
    /// <summary>True for the state an instance starts in.</summary>
    public bool IsInitial { get; init; }

    /// <summary>True when no transition leaves the state.</summary>
    public bool IsFinal { get; init; }

    /// <summary>True when the state is committed: once the server puts an instance there, it cannot be discarded.</summary>
    public bool IsCommitted { get; init; }

    /// <summary>The train the state invokes on entry (its interface's full name), or null.</summary>
    public string? InvokesTrain { get; init; }

    /// <summary>The machine the state spawns on entry (its id), or null.</summary>
    public string? SpawnsMachine { get; init; }
}

/// <summary>One transition of a <see cref="MachineDefinition"/>.</summary>
/// <param name="From">The state it leaves.</param>
/// <param name="Command">What takes it: a command's name, the timer's trigger, or an outcome trigger (<c>Building.done</c>).</param>
/// <param name="To">The state it enters.</param>
/// <param name="Kind">Whether a command, a timer or an outcome takes it.</param>
public record MachineDefinitionTransition(
    string From,
    string Command,
    string To,
    MachineTransitionKind Kind
)
{
    /// <summary>The guard's message, or the name of its rule; null when unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>The key prefix of the effect the transition runs exactly once, or null.</summary>
    public string? Effect { get; init; }

    /// <summary>For a timer, how long its state is held (ISO-8601, or a context field with its bounds); null otherwise.</summary>
    public string? After { get; init; }
}

/// <summary>One timed state's timer.</summary>
/// <param name="State">The timed state.</param>
/// <param name="Command">The timer's trigger.</param>
/// <param name="Target">The state the timer moves an instance to.</param>
public record MachineDefinitionTimer(string State, string Command, string Target)
{
    /// <summary>The fixed duration (ISO-8601), or null when a context field holds it.</summary>
    public string? After { get; init; }

    /// <summary>The context field that holds the duration, or null for a fixed one.</summary>
    public string? Field { get; init; }

    /// <summary>The shortest duration the field may hold (ISO-8601), or null.</summary>
    public string? Min { get; init; }

    /// <summary>The longest duration the field may hold (ISO-8601), or null.</summary>
    public string? Max { get; init; }
}
