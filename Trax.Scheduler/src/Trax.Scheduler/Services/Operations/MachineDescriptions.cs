namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One state machine the host registers, as <see cref="IOperationsService.GetMachinesAsync"/> describes it: its
/// name, version and graph, whether or not it has any instances.
/// </summary>
/// <remarks>
/// The graph is read from the machine's IR (<c>IMachine.ExportIr()</c>) by the same reading
/// <c>trax machine show</c> draws it from, so the two never disagree. It carries no context, no input and no
/// instance: only what the machine declares.
/// </remarks>
/// <param name="Name">The machine's id.</param>
/// <param name="Version">The definition version the machine declares.</param>
/// <param name="SystemOwned">True when only the system holds its instances.</param>
public record MachineDescription(string Name, int Version, bool SystemOwned)
{
    /// <summary>
    /// The hash of the machine's IR, which every instance row records as the definition that last wrote it; null for
    /// a machine with no exportable IR.
    /// </summary>
    public string? SchemaHash { get; init; }

    /// <summary>The state an instance starts in; null when the machine has no exportable IR.</summary>
    public string? InitialState { get; init; }

    /// <summary>Every state, in the order the IR lists them. Empty when the machine has no exportable IR.</summary>
    public IReadOnlyList<MachineStateDescription> States { get; init; } = [];

    /// <summary>
    /// Every transition: the commands first, then the timers, then each invoking state's outcomes in the order they
    /// are tried. Empty when the machine has no exportable IR.
    /// </summary>
    public IReadOnlyList<MachineTransitionDescription> Transitions { get; init; } = [];

    /// <summary>Every timed state's timer. Empty when the machine has none, or no exportable IR.</summary>
    public IReadOnlyList<MachineTimerDescription> Timers { get; init; } = [];

    /// <summary>
    /// Why the machine has no graph: it was declared with C# delegates, which its IR cannot carry. Null when it has one.
    /// </summary>
    public string? GraphUnavailableReason { get; init; }
}

/// <summary>One state of a <see cref="MachineDescription"/>.</summary>
/// <param name="Name">The state's name.</param>
public record MachineStateDescription(string Name)
{
    /// <summary>True for the state an instance starts in.</summary>
    public bool IsInitial { get; init; }

    /// <summary>True when no transition leaves the state: an instance that reaches it stays there.</summary>
    public bool IsFinal { get; init; }

    /// <summary>True when the state is committed: once the server puts an instance there, it cannot be discarded.</summary>
    public bool IsCommitted { get; init; }

    /// <summary>The train the state invokes on entry (its interface's full name), or null when it invokes none.</summary>
    public string? InvokesTrain { get; init; }

    /// <summary>The machine the state spawns on entry (its id), or null when it spawns none.</summary>
    public string? SpawnsMachine { get; init; }
}

/// <summary>What takes a transition.</summary>
public enum MachineTransitionKind
{
    /// <summary>A command a caller sends.</summary>
    Command,

    /// <summary>The state's timer, which only the server fires.</summary>
    Timer,

    /// <summary>The outcome of the train the state invoked, or of the machine it spawned.</summary>
    Outcome,
}

/// <summary>One transition of a <see cref="MachineDescription"/>.</summary>
/// <param name="From">The state it leaves.</param>
/// <param name="Command">
/// What takes it: the command's name, the timer's trigger, or the outcome trigger (<c>Building.done</c>,
/// <c>.failed</c>, <c>.cancelled</c>).
/// </param>
/// <param name="To">The state it enters.</param>
/// <param name="Kind">Whether a command, a timer or an outcome takes it.</param>
public record MachineTransitionDescription(
    string From,
    string Command,
    string To,
    MachineTransitionKind Kind
)
{
    /// <summary>The guard's message, or the name of its rule; null when the transition is unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>
    /// The key prefix of the effect the transition runs exactly once, or null when it runs none.
    /// </summary>
    public string? Effect { get; init; }

    /// <summary>
    /// For a timer, how long its state is held: an ISO-8601 duration, or the context field that holds it with its
    /// bounds. Null for a command or an outcome.
    /// </summary>
    public string? After { get; init; }
}

/// <summary>One timed state's timer.</summary>
/// <param name="State">The timed state.</param>
/// <param name="Command">The timer's trigger.</param>
/// <param name="Target">The state the timer moves an instance to.</param>
public record MachineTimerDescription(string State, string Command, string Target)
{
    /// <summary>The fixed duration (ISO-8601), or null when a context field holds it.</summary>
    public string? After { get; init; }

    /// <summary>The context field that holds the duration, or null for a fixed one.</summary>
    public string? Field { get; init; }

    /// <summary>The shortest duration the field may hold (ISO-8601), or null for a fixed one.</summary>
    public string? Min { get; init; }

    /// <summary>The longest duration the field may hold (ISO-8601), or null for a fixed one.</summary>
    public string? Max { get; init; }
}
