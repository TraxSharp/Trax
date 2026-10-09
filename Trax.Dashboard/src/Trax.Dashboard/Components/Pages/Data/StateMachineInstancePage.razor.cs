using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Radzen;
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

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private ILogger<StateMachineInstancePage> Logger { get; set; } = default!;

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

    // The runs the instance invoked, as the API's machineInstance { invokedRuns } reads them.
    private MachineInstanceRuns? _runs;

    private bool _confirmingCancel;
    private bool _cancelling;

    // The cancel's refusal, in the service's words, which the API returns too.
    private string? _actionError;

    /// <inheritdoc/>
    /// <remarks>The whole key: machine, owner kind, id and row id.</remarks>
    private protected override object? GetRouteKey() => (Machine, Owner, InstanceId, RowId);

    /// <inheritdoc/>
    /// <remarks>Drops the previous instance, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged()
    {
        _instance = null;
        _runs = null;
        _refusal = null;
        _confirmingCancel = false;
        _actionError = null;
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

        var instance = await OperationsService.GetMachineInstanceAsync(key, cancellationToken);
        _runs = instance is null
            ? null
            : await OperationsService.GetMachineInstanceRunsAsync(
                key with
                {
                    RowId = instance.RowId,
                },
                cancellationToken
            );
        _instance = instance;
    }

    /// <summary>
    /// Cancels the instance through <see cref="IOperationsService.CancelMachineInstanceAsync"/>,
    /// the call the API's <c>cancelMachineInstance</c> makes, after the operator has confirmed.
    /// A refusal is shown in the service's words. An exception is logged and shown as a generic
    /// message, because its text can name the host's internals.
    /// </summary>
    internal async Task CancelInstance()
    {
        if (_instance is null || Key() is not { } key)
            return;

        _actionError = null;
        _cancelling = true;

        try
        {
            var result = await OperationsService.CancelMachineInstanceAsync(key, DisposalToken);
            _confirmingCancel = false;

            if (!result.Success)
            {
                _actionError = result.Message;
                return;
            }

            await LoadDataAsync(DisposalToken);
            NotificationService.Notify(
                NotificationSeverity.Success,
                "Cancel Sent",
                result.Message,
                duration: 6000
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Could not cancel instance {Id} of state machine {Machine}",
                InstanceId,
                Machine
            );
            _confirmingCancel = false;
            _actionError =
                "The cancel could not be sent. The error has been logged; try again shortly.";
        }
        finally
        {
            _cancelling = false;
        }
    }
}
