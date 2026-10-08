using Microsoft.AspNetCore.Components;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one state-machine instance, at
/// <c>/trax/data/state-machines/{machine}/{system|user}/{id}</c>, with <c>?row={rowId}</c> for a
/// user's draft. It shows the instance's state, version, timestamps, owner kind and whether it
/// waits on a train run it invoked; never its context, and never the user who owns it.
/// Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class StateMachineInstancePage
{
    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>The machine's id, from the route.</summary>
    [Parameter]
    public string Machine { get; set; } = "";

    /// <summary>Who owns the instance, <c>system</c> or <c>user</c>, from the route.</summary>
    [Parameter]
    public string Owner { get; set; } = "";

    /// <summary>The instance or draft id, from the route.</summary>
    [Parameter]
    public Guid InstanceId { get; set; }

    /// <summary>
    /// The row id, from the query string; required for a user's draft, because several users can
    /// each hold a draft under one id.
    /// </summary>
    [SupplyParameterFromQuery(Name = "row")]
    public long? RowId { get; set; }

    // The instance as the operations service reads it for an operator: the API's machineInstance
    // reads the same call. It has no context field to show.
    private MachineInstanceRecord? _instance;

    // Why the route names no instance the service can look up, shown instead of "not found".
    private string? _refusal;

    /// <inheritdoc/>
    /// <remarks>The whole key: machine, owner kind, id and row id.</remarks>
    private protected override object? GetRouteKey() => (Machine, Owner, InstanceId, RowId);

    /// <inheritdoc/>
    /// <remarks>Drops the previous instance, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged()
    {
        _instance = null;
        _refusal = null;
    }

    /// <summary>
    /// The key this page's route names, or null when it names none the service can look up: an
    /// owner other than <c>system</c> or <c>user</c>, or a user's draft without its row id.
    /// </summary>
    internal MachineInstanceKey? Key()
    {
        var ownerKind = MachineInstanceRoutes.ParseOwnerSegment(Owner);
        if (ownerKind is null || (ownerKind == SnapshotOwnerKind.User && RowId is null))
            return null;
        return new MachineInstanceKey(Machine, ownerKind.Value, InstanceId, RowId);
    }

    /// <summary>
    /// Loads the instance through <see cref="IOperationsService.GetMachineInstanceAsync"/>, the
    /// read the API's <c>machineInstance</c> makes.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        if (Key() is not { } key)
        {
            _refusal = MachineInstanceRoutes.ParseOwnerSegment(Owner) is null
                ? $"'{Owner}' is not an owner: an instance is owned by the system or by a user."
                : "A user's draft is named by its row as well as its id, because several users "
                    + "can each hold a draft under one id. Open it from the State Machines list.";
            return;
        }

        _instance = await OperationsService.GetMachineInstanceAsync(key, cancellationToken);
    }
}
