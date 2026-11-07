using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

// The machines a host registers, with their graphs, read through IOperationsService as the console's Machines page
// reads them (central docs/0022), under the operations field and so behind the host's operations gate.
public partial class OperationsQueries
{
    /// <summary>
    /// Every state machine the host registers, ordered by name, whether or not it has any
    /// instances: its version and its graph, read from its IR by the same reading
    /// <c>trax machine show</c> draws it from. Each state says whether it is initial, final or
    /// committed, and which train it invokes or machine it spawns; each transition says what takes
    /// it (<c>COMMAND</c>, <c>TIMER</c> or <c>OUTCOME</c>) and its guard; each timer how long its
    /// state is held. A machine declared with C# delegates has no IR, and is listed with
    /// <c>graphUnavailableReason</c> instead of a graph.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    public async Task<IReadOnlyList<MachineDefinition>> GetMachines(
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => (await operationsService.GetMachinesAsync(ct)).Select(MachineDefinition.From).ToList();

    /// <summary>
    /// The event-storming board of a machine this host serves from features: who acts, what they ask, what happens,
    /// the rules between, the views, and the scenarios behind each, one lane per machine it joins. Null for a machine
    /// not served from features. A scenario is its file, line and name, never its steps.
    /// </summary>
    /// <param name="machine">The machine's id.</param>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    public async Task<MachineBoard?> GetMachineBoard(
        string machine,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) =>
        await operationsService.GetMachineBoardAsync(machine, ct) is { } board
            ? MachineBoard.From(board)
            : null;
}
