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
