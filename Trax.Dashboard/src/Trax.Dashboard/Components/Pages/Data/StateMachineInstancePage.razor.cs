using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one state-machine instance, at
/// <c>/trax/data/state-machines/{machine}/{system|user}/{id}</c>, with <c>?row={rowId}</c> for a
/// user's draft. It shows the instance's state, revision, version, timestamps, owner kind and
/// whether it waits on a train run it invoked; never its context, and never the user who owns it.
/// Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
/// <remarks>
/// It is live: once loaded it follows <see cref="IOperationsService.WatchMachineInstancesAsync"/>,
/// the feed the API's <c>onMachineInstanceChanged</c> reads, so a write shows as it happens rather
/// than at the next poll. Leaving the page ends its feed, and so does the circuit's operator losing
/// the dashboard's posture, which the circuit re-checks when the user changes and on its
/// revalidation interval.
/// </remarks>
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

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

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

    // The live feed this page follows, ended when the page goes, the route changes, or the circuit
    // is refused.
    private CancellationTokenSource? _feed;

    // The circuit's re-check of the dashboard's posture, when the host registers one.
    private DashboardCircuitAuthorization? _authorization;

    // Whether the feed reported the instance deleted or expired.
    private bool _deleted;

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
        _deleted = false;
        StopFeed();
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
        var runs = instance is null
            ? null
            : await OperationsService.GetMachineInstanceRunsAsync(
                key with
                {
                    RowId = instance.RowId,
                },
                cancellationToken
            );

        // The feed may have shown a newer revision than this read found; the page never goes back.
        if (
            instance is not null
            && _instance is { Revision: { } shown }
            && _instance.RowId == instance.RowId
            && instance.Revision is { } read
            && read < shown
        )
            return;

        _runs = runs;
        _instance = instance;
        if (instance is not null)
            _deleted = false;
        FollowFeed(key);
    }

    /// <summary>
    /// Starts following the live feed for this page's instance, once: from the operations service,
    /// for the page's machine, until the page goes or the circuit is refused.
    /// </summary>
    private void FollowFeed(MachineInstanceKey key)
    {
        if (_feed is not null)
            return;

        _authorization ??= Services.GetService<DashboardCircuitAuthorization>();
        if (_authorization is not null)
        {
            _authorization.Attach();
            if (_authorization.IsRefused)
                return;
            _authorization.Refused -= StopFeed;
            _authorization.Refused += StopFeed;
        }

        var feed = new CancellationTokenSource();
        _feed = feed;
        _ = ReadFeedAsync(key, feed);
    }

    private async Task ReadFeedAsync(MachineInstanceKey key, CancellationTokenSource feed)
    {
        var ct = feed.Token;
        try
        {
            await foreach (
                var record in OperationsService
                    .WatchMachineInstancesAsync(key.Machine, ct)
                    .WithCancellation(ct)
            )
            {
                // Writes may have been missed (the host's listener started or reconnected): read
                // the instance again rather than wait for the next poll.
                if (record.Reload)
                {
                    if (ct.IsCancellationRequested || _authorization is { IsRefused: true })
                        return;
                    await InvokeAsync(async () =>
                    {
                        await LoadDataAsync(ct);
                        StateHasChanged();
                    });
                    continue;
                }
                if (
                    record.OwnerKind != key.OwnerKind
                    || record.Id != key.Id
                    || (key.RowId is { } row && record.RowId != row)
                )
                    continue;
                if (ct.IsCancellationRequested || _authorization is { IsRefused: true })
                    return;

                await InvokeAsync(async () => await ShowAsync(key, record, ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The page left, the route changed, or the circuit was refused.
        }
        catch (Exception ex)
        {
            // The page still has its poll; the feed is a faster way to the same rows.
            Logger.LogWarning(
                ex,
                "The live feed for instance {Id} of state machine {Machine} ended; the page falls back to polling.",
                key.Id,
                key.Machine
            );
        }
        finally
        {
            if (ReferenceEquals(_feed, feed))
                _feed = null;
        }
    }

    // Shows one record the feed sent for this instance, reading its runs again when its state
    // moved; a revision at or below the one shown is dropped.
    private async Task ShowAsync(
        MachineInstanceKey key,
        MachineInstanceRecord record,
        CancellationToken ct
    )
    {
        if (ct.IsCancellationRequested)
            return;

        if (record.Deleted)
        {
            if (_instance is not null && _instance.RowId != record.RowId)
                return;
            _instance = null;
            _runs = null;
            _deleted = true;
            _confirmingCancel = false;
            StateHasChanged();
            return;
        }

        if (
            _instance is { } shown
            && shown.RowId == record.RowId
            && shown.Revision is { } held
            && record.Revision is { } next
            && next <= held
        )
            return;

        var moved = _instance is null || _instance.State != record.State;
        _instance = record;
        _deleted = false;
        StateHasChanged();

        if (!moved)
            return;
        try
        {
            _runs = await OperationsService.GetMachineInstanceRunsAsync(
                key with
                {
                    RowId = record.RowId,
                },
                ct
            );
            StateHasChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(
                ex,
                "Could not read the runs of instance {Id} of state machine {Machine}; the next poll will.",
                key.Id,
                key.Machine
            );
        }
    }

    private void StopFeed()
    {
        var feed = _feed;
        _feed = null;
        feed?.Cancel();
    }

    /// <inheritdoc/>
    /// <remarks>Ends the live feed as well.</remarks>
    public override ValueTask DisposeAsync()
    {
        if (_authorization is not null)
            _authorization.Refused -= StopFeed;
        StopFeed();
        return base.DisposeAsync();
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
