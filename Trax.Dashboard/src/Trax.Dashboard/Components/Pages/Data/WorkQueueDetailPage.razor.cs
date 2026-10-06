using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Utilities;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one work queue entry, at <c>/trax/data/work-queue/{id}</c>. For a queued entry
/// with a subject key it names what it is waiting on: the in-flight run holding its subject, or an
/// older queued sibling that dispatch will take first. The user can cancel a queued entry.
/// Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class WorkQueueDetailPage
{
    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    /// <summary>The work queue entry's database id, from the route.</summary>
    [Parameter]
    public long WorkQueueId { get; set; }

    // The entry as the operations service reads it, with its [TraxSensitive] input members
    // masked (the stored copy keeps them in clear because the run reads it) and what a queued
    // entry with a subject is waiting on. The API's workQueue.detail reads the same call.
    private WorkQueueEntryDetail? _entry;
    private bool _cancelling;
    private string? _error;

    // The input is re-indented once per change, not on every render.
    private readonly JsonDisplayCache _json = new();

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="WorkQueueId"/>.</remarks>
    private protected override object? GetRouteKey() => WorkQueueId;

    /// <inheritdoc/>
    /// <remarks>Drops the previous entry, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged()
    {
        _entry = null;
    }

    /// <summary>
    /// Loads the entry through <see cref="IOperationsService.GetWorkQueueEntryDetailAsync"/>: its
    /// masked input and, when it is queued with a subject key, the id of the dispatched entry whose
    /// run still holds the subject or, failing that, of the queued sibling ahead of it.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken) =>
        _entry = await OperationsService.GetWorkQueueEntryDetailAsync(
            WorkQueueId,
            cancellationToken
        );

    private async Task CancelEntry()
    {
        if (_entry is null)
            return;

        _error = null;
        _cancelling = true;

        try
        {
            var result = await OperationsService.CancelWorkQueueEntryAsync(
                WorkQueueId,
                DisposalToken
            );

            if (!result.Success)
            {
                _error = result.Message;
                return;
            }

            // Reload so the UI reflects the new status without a full page navigation.
            await LoadDataAsync(DisposalToken);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Entry Cancelled",
                $"Work queue entry {WorkQueueId} has been cancelled.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _cancelling = false;
        }
    }
}
