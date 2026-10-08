using Radzen;
using Trax.Dashboard.Models;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// How the State machines grid reads a page: through
/// <see cref="IOperationsService.GetMachineInstancesAsync"/> and
/// <see cref="IOperationsService.CountMachineInstancesAsync"/>, the calls the API's
/// <c>machineInstances</c> query makes, with the filter the page's own filter bar sets. Kept here
/// rather than in the page so a test reads what the grid reads.
/// </summary>
internal static class MachineInstanceGridQuery
{
    /// <summary>
    /// Reads the page <paramref name="args"/> asks for. Only its skip and take are read: the list
    /// is always newest first, and the service serves only the filters in
    /// <paramref name="filter"/>.
    /// </summary>
    /// <param name="operations">The operations service.</param>
    /// <param name="filter">The machine, state and owner kind; its paging fields are ignored.</param>
    /// <param name="args">The grid's page.</param>
    /// <param name="count">The grid's remembered total; see <see cref="GridCount"/>.</param>
    /// <param name="capped">
    /// Set when the total was last counted: whether more instances match than the service counts
    /// (<see cref="OperationsService.MachineInstanceCountCap"/>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<ServerDataResult<MachineInstanceRow>> LoadPageAsync(
        IOperationsService operations,
        MachineInstanceQuery filter,
        LoadDataArgs args,
        GridCount count,
        MachineInstanceCountCapped capped,
        CancellationToken ct
    )
    {
        var page = filter with { Skip = args.Skip ?? 0, Take = args.Top ?? 20 };

        return DataGridQueryHelper.LoadPageAsync<MachineInstanceRow>(
            async token =>
                (await operations.GetMachineInstancesAsync(page, token))
                    .Items.Select(MachineInstanceRow.From)
                    .ToList(),
            async token =>
            {
                var total = await operations.CountMachineInstancesAsync(filter, token);
                capped.Value = total.Capped;
                return total.Count;
            },
            string.Join('\u001f', filter.Machine, filter.State, filter.OwnerKind),
            args,
            count,
            ct
        );
    }
}

/// <summary>
/// Whether the State machines grid's total is the service's cap rather than the number of
/// matching instances.
/// </summary>
internal sealed class MachineInstanceCountCapped
{
    public bool Value { get; set; }
}

/// <summary>
/// The dashboard paths of the State machines pages. An instance's path names its owner kind, so a
/// user's draft and a system instance under the same id never share one, and a user's draft
/// carries its row id, because several users can hold one id.
/// </summary>
internal static class MachineInstanceRoutes
{
    /// <summary>The list page.</summary>
    public const string List = "trax/data/state-machines";

    /// <summary>The page of the instance <paramref name="row"/> shows.</summary>
    public static string Detail(MachineInstanceRow row) =>
        Detail(row.Machine, row.OwnerKind, row.Id, row.RowId);

    /// <summary>The page of one instance.</summary>
    public static string Detail(string machine, SnapshotOwnerKind ownerKind, Guid id, long rowId)
    {
        var path = $"{List}/{Uri.EscapeDataString(machine)}/{OwnerSegment(ownerKind)}/{id}";
        // A system instance is unique by machine and id; the row id is what tells users' drafts
        // apart.
        return ownerKind == SnapshotOwnerKind.User ? $"{path}?row={rowId}" : path;
    }

    /// <summary>The path segment for <paramref name="ownerKind"/>.</summary>
    public static string OwnerSegment(SnapshotOwnerKind ownerKind) =>
        ownerKind == SnapshotOwnerKind.System ? "system" : "user";

    /// <summary>The owner kind a path segment names, or null for any other text.</summary>
    public static SnapshotOwnerKind? ParseOwnerSegment(string? segment) =>
        segment switch
        {
            "system" => SnapshotOwnerKind.System,
            "user" => SnapshotOwnerKind.User,
            _ => null,
        };
}
