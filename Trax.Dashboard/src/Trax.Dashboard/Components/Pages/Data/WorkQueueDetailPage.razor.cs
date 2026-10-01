using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Services.TrainDiscovery;
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
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private ITrainDiscoveryService TrainDiscovery { get; set; } = default!;

    /// <summary>The work queue entry's database id, from the route.</summary>
    [Parameter]
    public long WorkQueueId { get; set; }

    private WorkQueue? _entry;
    private long? _subjectHeldBy;
    private long? _subjectQueuedBehind;
    private bool _cancelling;
    private string? _error;

    // The entry's input with its [TraxSensitive] members masked: the stored copy keeps them in
    // clear because the run reads it.
    private string? _maskedInput;

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
        _maskedInput = null;
    }

    /// <summary>
    /// Loads the entry and, when it is queued with a subject key, the id of the dispatched entry
    /// whose run still holds the subject or, failing that, of the queued sibling ahead of it.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);
        _entry = await context
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == WorkQueueId, cancellationToken);

        _maskedInput = TransportInputRedaction.Redact(
            TrainDiscovery,
            _entry?.Input,
            _entry?.InputTypeName
        );

        // A queued entry whose subject has a run in flight is skipped by dispatch until that run
        // finishes. Without saying so it looks like an entry that is simply never picked up.
        _subjectHeldBy = _entry is { Status: WorkQueueStatus.Queued, SubjectKey: { } subject }
            ? await context
                .WorkQueues.AsNoTracking()
                .Where(b =>
                    b.SubjectKey == subject
                    && b.Status == WorkQueueStatus.Dispatched
                    && b.Metadata != null
                    && (
                        b.Metadata.TrainState == TrainState.Pending
                        || b.Metadata.TrainState == TrainState.InProgress
                    )
                )
                .Select(b => (long?)b.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        // Dispatch also offers only the first queued entry per subject each cycle, so an entry
        // behind an older sibling waits even while nothing for its subject is running.
        var now = DateTime.UtcNow;

        _subjectQueuedBehind =
            _subjectHeldBy is null
            && _entry is { Status: WorkQueueStatus.Queued, SubjectKey: not null }
                ? await context
                    .WorkQueues.AsNoTracking()
                    .DispatchedAheadOf(_entry, now)
                    .Select(b => (long?)b.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
    }

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
