using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Metadata;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one dead letter, at <c>/trax/data/dead-letters/{id}</c>: the dead letter, its
/// manifest, and the manifest's failed runs. From here the user can re-queue the manifest, which
/// goes through the scheduler and opens the new work queue entry, or acknowledge the dead letter
/// with a note. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class DeadLetterDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ITraxScheduler Scheduler { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private ITrainDiscoveryService TrainDiscovery { get; set; } = default!;

    /// <summary>The dead letter's database id, from the route.</summary>
    [Parameter]
    public long DeadLetterId { get; set; }

    private DeadLetter? _deadLetter;

    // The manifest's properties with their [TraxSensitive] members masked: the stored copy keeps
    // them in clear because every run of the manifest reads it.
    private string? _maskedManifestProperties;
    private int _failedRunCount;
    private TraxDataGrid<RunRow>? _failedRunsGrid;
    private readonly GridCount _failedRunsCount = new();

    // The JSON shown is re-indented once per change, not on every render.
    private readonly JsonDisplayCache _json = new();

    // The row without the run's input, output and stack trace, which the grid does not show.
    private Task<ServerDataResult<RunRow>> LoadFailedRunsPageAsync(
        LoadDataArgs args,
        CancellationToken ct
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            DataContextFactory,
            db =>
                db.Metadatas.AsNoTracking()
                    .Where(m =>
                        m.ManifestId == _deadLetter!.ManifestId && m.TrainState == TrainState.Failed
                    )
                    .OrderByDescending(m => m.StartTime),
            RunRow.Projection,
            args,
            _failedRunsCount,
            DeadLetterId,
            ct
        );

    private Metadata? _latestFailedRun;

    private bool _requeueing;

    // Which of the two re-queue buttons is busy while _requeueing.
    private bool _requeueingAskAfresh;
    private bool _acknowledging;
    private bool _showAcknowledgeInput;
    private string _acknowledgeNote = "";
    private string? _actionError;

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="DeadLetterId"/>.</remarks>
    private protected override object? GetRouteKey() => DeadLetterId;

    /// <inheritdoc/>
    /// <remarks>Drops the previous dead letter, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged()
    {
        _deadLetter = null;
        _maskedManifestProperties = null;
        _latestFailedRun = null;
    }

    /// <summary>
    /// Loads the dead letter with its manifest, the count and most recent of the manifest's failed
    /// runs, and reloads the failed-runs grid, which pages its rows from the database. Leaves the
    /// page empty when no dead letter has the id.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _deadLetter = await context
            .DeadLetters.Include(d => d.Manifest)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == DeadLetterId, cancellationToken);

        _maskedManifestProperties = TransportInputRedaction.Redact(
            TrainDiscovery,
            _deadLetter?.Manifest?.Properties,
            _deadLetter?.Manifest?.PropertyTypeName
        );

        if (_deadLetter is not null)
        {
            var failedRuns = context
                .Metadatas.AsNoTracking()
                .Where(m =>
                    m.ManifestId == _deadLetter.ManifestId && m.TrainState == TrainState.Failed
                );

            // The grid pages the failed runs from the database; the page reads only the count
            // and the most recent one.
            _failedRunCount = await failedRuns.CountAsync(cancellationToken);

            // The most recent failed run is shown whole, input and stack trace included. A failed
            // run does not change, so it is read again only when a newer one takes its place.
            var latestId = await failedRuns
                .OrderByDescending(m => m.StartTime)
                .Select(m => (long?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestId != _latestFailedRun?.Id)
                _latestFailedRun = latestId is null
                    ? null
                    : await context
                        .Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == latestId, cancellationToken);

            if (_failedRunsGrid is not null)
                await _failedRunsGrid.ReloadAsync();
        }
    }

    // Asking afresh uses the scheduler's askAfresh overload, as the API's requeueDeadLetter does
    // with askAfresh set; the default re-queue keeps its own overload.
    private async Task RequeueManifest(bool askAfresh)
    {
        if (_deadLetter is null)
            return;

        _actionError = null;
        _requeueing = true;
        _requeueingAskAfresh = askAfresh;

        try
        {
            var result = askAfresh
                ? await Scheduler.RequeueDeadLetterAsync(
                    DeadLetterId,
                    askAfresh: true,
                    DisposalToken
                )
                : await Scheduler.RequeueDeadLetterAsync(DeadLetterId, DisposalToken);

            if (!result.Success)
            {
                _actionError = result.Message;
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Re-queued",
                $"{ShortName(_deadLetter.Manifest?.Name ?? "Unknown")} has been re-queued (WorkQueue ID {result.WorkQueueId}).",
                duration: 4000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{result.WorkQueueId}");
        }
        catch (Exception ex)
        {
            _actionError = ex.Message;
        }
        finally
        {
            _requeueing = false;
        }
    }

    private async Task AcknowledgeDeadLetter()
    {
        if (_deadLetter is null)
            return;

        _actionError = null;
        _acknowledging = true;

        try
        {
            var result = await Scheduler.AcknowledgeDeadLetterAsync(
                DeadLetterId,
                _acknowledgeNote,
                DisposalToken
            );

            if (!result.Success)
            {
                _actionError = result.Message;
                return;
            }

            _showAcknowledgeInput = false;
            _acknowledgeNote = "";

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Dead Letter Acknowledged",
                $"Dead letter #{DeadLetterId} has been acknowledged.",
                duration: 4000
            );

            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            _actionError = ex.Message;
        }
        finally
        {
            _acknowledging = false;
        }
    }
}
