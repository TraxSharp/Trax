using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one manifest, at <c>/trax/data/manifests/{id}</c>: its schedule, exclusions,
/// group, run counts by state and a paged grid of its runs. The user can trigger the manifest to
/// run now through the scheduler. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class ManifestDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    /// <summary>The manifest's database id, from the route.</summary>
    [Parameter]
    public long ManifestId { get; set; }

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="ManifestId"/>.</remarks>
    private protected override object? GetRouteKey() => ManifestId;

    /// <inheritdoc/>
    /// <remarks>Drops the previous manifest, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged() => _manifest = null;

    private Manifest? _manifest;
    private TraxDataGrid<RunRow>? _runsGrid;
    private readonly GridCount _runsCount = new();
    private long _totalRuns;
    private long _completedRuns;
    private long _failedRuns;
    private long _inProgressRuns;
    private List<Exclusion> _exclusions = [];
    private bool _triggering;

    // Which of the two run buttons is busy while _triggering.
    private bool _triggeringAskAfresh;
    private string? _triggerError;
    private bool _settingReplay;
    private string? _replayError;

    // Bumped when a replay-on-retry change is not saved, so the switch is rebuilt showing the
    // stored value rather than the one it flipped to.
    private int _replaySwitchVersion;

    /// <summary>
    /// Loads the manifest with its group and exclusions, counts its runs by state over all time,
    /// and reloads the runs grid, which pages its rows from the database. Leaves the page empty
    /// when no manifest has the id.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _manifest = await context
            .Manifests.Include(m => m.ManifestGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ManifestId, cancellationToken);

        if (_manifest is not null)
        {
            _exclusions = _manifest.GetExclusions();

            // Counted over every run of the manifest by the call the API's manifestStats makes,
            // rather than over the page of runs the grid shows.
            var stats = await OperationsService.GetManifestExecutionStatsAsync(
                ManifestId,
                cancellationToken
            );
            _totalRuns = stats.Total;
            _completedRuns = stats.Completed;
            _failedRuns = stats.Failed;
            _inProgressRuns = stats.InProgress;

            if (_runsGrid is not null)
                await _runsGrid.ReloadAsync();
        }
    }

    // The row without the run's input, output and stack trace, which the grid does not show.
    private Task<ServerDataResult<RunRow>> LoadRunsPageAsync(
        LoadDataArgs args,
        CancellationToken ct
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            DataContextFactory,
            db =>
                db.Metadatas.AsNoTracking()
                    .Where(m => m.ManifestId == ManifestId)
                    .OrderByDescending(m => m.StartTime),
            RunRow.Projection,
            args,
            _runsCount,
            ManifestId,
            ct
        );

    private static string FormatExclusion(Exclusion exclusion)
    {
        return exclusion.Type switch
        {
            ExclusionType.DaysOfWeek when exclusion.DaysOfWeek is not null => string.Join(
                ", ",
                exclusion.DaysOfWeek
            ),
            ExclusionType.Dates when exclusion.Dates is not null => string.Join(
                ", ",
                exclusion.Dates.Select(d => d.ToString("yyyy-MM-dd"))
            ),
            ExclusionType.DateRange =>
                $"{exclusion.StartDate?.ToString("yyyy-MM-dd")} to {exclusion.EndDate?.ToString("yyyy-MM-dd")}",
            ExclusionType.TimeWindow =>
                $"{exclusion.StartTime?.ToString("HH:mm")} to {exclusion.EndTime?.ToString("HH:mm")} daily",
            _ => "Unknown",
        };
    }

    // Through the operations service, as the API's triggerManifest is, so both say the same thing
    // about what the trigger did: queued a new run, brought a queued one forward, or released one
    // already due. Asked afresh, a queued retry the dispatcher had already claimed still replays,
    // and the notification says so rather than report an ask-afresh that did not happen.
    private async Task TriggerManifest(bool askAfresh)
    {
        if (_manifest is null)
            return;

        _triggerError = null;
        _triggering = true;
        _triggeringAskAfresh = askAfresh;

        try
        {
            var result = await OperationsService.TriggerManifestAsync(
                _manifest.ExternalId,
                delay: null,
                askAfresh,
                DisposalToken
            );

            if (!result.Success)
                _triggerError = result.Message;
            else if (result.StillReplaying)
                NotificationService.Notify(
                    NotificationSeverity.Warning,
                    "Train Queued, Still Replaying",
                    result.Message,
                    duration: 10000
                );
            else
                NotificationService.Notify(
                    NotificationSeverity.Success,
                    "Train Queued",
                    result.Message,
                    duration: 6000
                );
        }
        catch (Exception ex)
        {
            _triggerError = ex.Message;
        }
        finally
        {
            _triggering = false;
        }
    }

    // Through the operations service, as the API's setManifestsReplayDecisionsOnRetry is, and
    // reported as the enable and disable actions report theirs: the service's message on success,
    // its refusal or the exception as an alert.
    private async Task SetReplayDecisionsOnRetry(bool replay)
    {
        if (_manifest is null)
            return;

        _replayError = null;
        _settingReplay = true;

        try
        {
            var result = await OperationsService.SetManifestsReplayDecisionsOnRetryAsync(
                [_manifest.Id],
                replay,
                DisposalToken
            );

            if (!result.Success)
            {
                _replayError = result.Message;
                _replaySwitchVersion++;
                return;
            }

            _manifest.ReplayDecisionsOnRetry = replay;
            NotificationService.Notify(
                NotificationSeverity.Success,
                replay ? "Retries Replay Decisions" : "Retries Ask Afresh",
                result.Message ?? "",
                duration: 4000
            );
            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            _replayError = ex.Message;
            _replaySwitchVersion++;
        }
        finally
        {
            _settingReplay = false;
        }
    }
}
