using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.DTOs;
using Trax.Api.Services.Runs;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.Checkpoints;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one run (metadata row), at <c>/trax/data/metadata/{id}</c>: its state, input,
/// output, failure details, its junction timeline (when the host records junction events) and a
/// paged grid of its logs. The user can cancel it while it is pending or in
/// progress, or queue the train again with the run's saved input. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class MetadataDetailPage
{
    [Inject]
    private ITrustedExecutionScope TrustedScope { get; set; } = default!;

    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private ITrainChainGraphs ChainGraphs { get; set; } = default!;

    // The resume check is read from here rather than injected: a host without checkpoints may not
    // register it, and its run page offers no resume.
    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    /// <summary>The run's (metadata row's) database id, from the route.</summary>
    [Parameter]
    public long MetadataId { get; set; }

    // The API's largest junctionRuns page, so the page never reads more steps per poll than one
    // API call returns.
    private const int MaxJunctionSteps = 500;

    private Metadata? _metadata;
    private IReadOnlyList<JunctionStep> _junctionRuns = [];
    private bool _moreJunctionSteps;

    // The run on its train's graph, placed from the steps above; placed again only when they change.
    private RunGraph? _runGraph;

    // How many loads have read the timeline since the run was first seen finished. A finished
    // run's steps are read in full twice, once when it is first seen finished and once more for a
    // step the writer stored a moment after the run ended, and then not again.
    private int _finishedTimelineReads;
    private int _logCount;
    private LogsGrid? _logsGrid;

    // The input and output are re-indented once per change, not on every render.
    private readonly JsonDisplayCache _json = new();

    // True while a re-queue or a resume is in flight: one at a time, from any button.
    private bool _rerunning;

    // Which button is busy while _rerunning, and for a resume from the run graph, which node's.
    private Busy _busy;
    private string? _resumingNode;

    private enum Busy
    {
        None,
        Requeue,
        RequeueAfresh,
        Resume,
        ResumeFromNode,
    }

    private string? _rerunError;
    private bool _cancelling;
    private string? _cancelError;

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="MetadataId"/>.</remarks>
    private protected override object? GetRouteKey() => MetadataId;

    /// <inheritdoc/>
    /// <remarks>Drops the previous run, so a failed reload does not show it under the new route.</remarks>
    private protected override void OnRouteKeyChanged()
    {
        _metadata = null;
        _junctionRuns = [];
        _moreJunctionSteps = false;
        _runGraph = null;
        _finishedTimelineReads = 0;
        _rerunError = null;
    }

    /// <summary>
    /// Loads the run and the number of its log entries, and reloads the logs grid, which pages its
    /// rows through the operations service, oldest first. Leaves the page empty when no run has the id.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _metadata = await context
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == MetadataId, cancellationToken);

        if (_metadata is not null)
        {
            // The grid pages its logs through the operations service, as the API's logs query
            // does; the page only needs to know whether there are any.
            _logCount = await OperationsService.CountLogsAsync(
                new LogQuery(MetadataId: MetadataId),
                cancellationToken
            );

            await LoadJunctionStepsAsync(context, _metadata.TrainState, cancellationToken);
            await PlaceRunGraphAsync(_metadata, cancellationToken);

            if (_logsGrid is not null)
                await _logsGrid.ReloadAsync();
        }
    }

    /// <summary>
    /// Reads the run's steps, through the query and the mapping the API's junctionRuns uses, so
    /// the two show the same rows with the same fields, and at most the API's page of them. Empty
    /// when the host did not call AddJunctionEvents().
    /// </summary>
    /// <remarks>
    /// <para>While the run is going, a poll reads only what can have changed: the steps after the
    /// last one held, and every step from the first one still in progress, whose row is updated in
    /// place when it ends. Once the run has finished, its steps are read in full on the load that
    /// first sees it finished and on the one after, and are then held as they are, so polling a
    /// finished run reads no steps and leaves the timeline unrendered.</para>
    /// <para>One more step than the page is read, so a run with more steps says so rather than
    /// ending its timeline silently.</para>
    /// </remarks>
    private async Task LoadJunctionStepsAsync(
        IDataContext context,
        TrainState runState,
        CancellationToken cancellationToken
    )
    {
        var finished =
            runState is TrainState.Completed or TrainState.Failed or TrainState.Cancelled;
        if (!finished)
            _finishedTimelineReads = 0;
        else if (_finishedTimelineReads >= 2)
            return;

        // Held steps before the first one still in progress cannot change; everything from there
        // on is read again. A finished run is read in full.
        var firstInProgress = _junctionRuns
            .Where(s => s.State == JunctionRunState.InProgress)
            .Select(s => (int?)s.Position)
            .FirstOrDefault();
        var kept = finished
            ? []
            : _junctionRuns
                .Where(s => firstInProgress is null || s.Position < firstInProgress)
                .ToList();

        // Every held step has ended and the run already says it has more than the page: nothing
        // the timeline shows can change until the run finishes.
        if (!finished && _moreJunctionSteps && kept.Count == _junctionRuns.Count)
            return;

        IQueryable<JunctionRun> query = context.JunctionRuns.AsNoTracking().ForRun(MetadataId);
        if (firstInProgress is { } from && !finished)
            query = query.Where(r => r.Position >= from);
        else if (kept.Count > 0)
        {
            var after = kept[^1].Position;
            query = query.Where(r => r.Position > after);
        }

        var rows = await query
            .Take(MaxJunctionSteps + 1 - kept.Count)
            .ToListAsync(cancellationToken);
        var steps = kept.Concat(rows.Select(JunctionStep.From)).ToList();

        _moreJunctionSteps = steps.Count > MaxJunctionSteps;
        if (_moreJunctionSteps)
            steps.RemoveRange(MaxJunctionSteps, steps.Count - MaxJunctionSteps);

        // The same list is kept when nothing changed, so the timeline can skip rendering it again.
        if (!steps.SequenceEqual(_junctionRuns))
            _junctionRuns = steps;

        if (finished)
            _finishedTimelineReads++;
    }

    /// <summary>
    /// Places the run's steps on its train's declared graph through <c>RunGraphs.Match</c>,
    /// the matching the API's runGraph makes after reading the same steps the same way, so the two
    /// show the same nodes in the same states. The graph comes from the registered train by name,
    /// read once per train and kept by <see cref="ITrainChainGraphs"/>.
    /// </summary>
    /// <remarks>
    /// <para>The page reuses the steps its timeline polls rather than reading them again, and
    /// places them only when they or the run's state changed, so a poll of a finished run does no
    /// work here either.</para>
    /// <para>For a failed or cancelled run, and a resumed one, the checkpoints it can resume from
    /// are read through <see cref="RunGraphs.ReadResumesAsync"/>, as the API's runGraph reads
    /// them, once per placement: which nodes offer "Resume from here", which hold a checkpoint,
    /// and which a resumed run restored. Never what a checkpoint holds.</para>
    /// </remarks>
    private async Task PlaceRunGraphAsync(Metadata run, CancellationToken cancellationToken)
    {
        if (
            _runGraph is not null
            && ReferenceEquals(_runGraphSteps, _junctionRuns)
            && _runGraph.MoreSteps == _moreJunctionSteps
            && _runGraphState == run.TrainState
        )
            return;

        var graph = ChainGraphs.Find(run.Name);
        var resumes = RunGraphs.ReadsResumes(run.TrainState, run.ResumeFrom)
            ? await RunGraphs.ReadResumesAsync(
                Services.GetService<IRunResumes>(),
                ChainGraphs,
                MetadataId,
                run.Name,
                graph,
                cancellationToken
            )
            : null;

        _runGraphSteps = _junctionRuns;
        _runGraphState = run.TrainState;
        _runGraph = RunGraphs.Match(
            MetadataId,
            run.Name,
            graph,
            _junctionRuns,
            _moreJunctionSteps,
            resumes,
            RunGraphs.Resumable(run),
            RunGraphs.Ended(run.TrainState)
        );
    }

    private IReadOnlyList<JunctionStep>? _runGraphSteps;
    private TrainState? _runGraphState;

    // Through the operations service, as the API's cancelExecution is: a Pending or InProgress
    // run is flagged, and one that finished since the page last loaded is reported as not
    // cancellable rather than as cancelled.
    private async Task CancelTrain()
    {
        if (
            _metadata is null
            || _metadata.TrainState is not (TrainState.Pending or TrainState.InProgress)
        )
            return;

        _cancelError = null;
        _cancelling = true;

        try
        {
            var result = await RunCancellation.CancelOneAsync(
                OperationsService,
                MetadataId,
                DisposalToken
            );

            if (!result.Success)
            {
                _cancelError = result.Message;
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Cancellation Requested",
                $"Cancellation requested for {ShortName(_metadata.Name)}.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _cancelError = ex.Message;
        }
        finally
        {
            _cancelling = false;
        }
    }

    private async Task RequeueTrain(bool askAfresh)
    {
        // Disabled on the buttons is not enough: a click the browser sent before it applied that
        // render still arrives, and each call queues a new run. Either button, either mode.
        if (_metadata is null || _rerunning)
            return;

        _rerunError = null;
        _rerunning = true;
        _busy = askAfresh ? Busy.RequeueAfresh : Busy.Requeue;

        try
        {
            // Through the operations service's re-queue, the call the API's requeueExecution
            // makes, so the two refuse the same runs with the same messages and enqueue the same
            // way: the saved input is checked (nothing saved, a placeholder saved in its place, or
            // masked [TraxSensitive] members would read back as defaults), then queued through the
            // mediator, so the train's OnQueue hook, subject key and input cap apply. When the run
            // recorded decisions, the new run replays them and takes the tracks this one took,
            // unless the operator asked afresh, the API's requeueExecution with askAfresh set:
            // then the new entry carries no replay link and the run asks every question again.
            OperationResult result;
            // The dashboard is the admin surface, gated as a whole by its host, so it enqueues as
            // trusted infrastructure rather than as a user a train's [TraxAuthorize] can check: a
            // Blazor circuit has no request to carry one. OnQueue, the subject key and the input
            // cap still apply. See docs/0017.
            using (TrustedScope.BeginTrusted("dashboard"))
                result = askAfresh
                    ? await OperationsService.RequeueExecutionAsync(
                        _metadata.Id,
                        askAfresh: true,
                        DisposalToken
                    )
                    : await OperationsService.RequeueExecutionAsync(_metadata.Id, DisposalToken);

            if (!result.Success || result.Id is not { } entryId)
            {
                _rerunError = result.Message;
                return;
            }

            // The service's message as the API returns it, which also says when a re-queue asks
            // afresh because this run's decisions are already replayed elsewhere.
            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Queued",
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"{ShortName(_metadata.Name)} has been re-queued (ID {entryId})."
                    : $"{ShortName(_metadata.Name)} has been re-queued. {result.Message}",
                duration: 8000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{entryId}");
        }
        catch (Exception ex)
        {
            _rerunError = ex.Message;
        }
        finally
        {
            _rerunning = false;
            _busy = Busy.None;
        }
    }

    // Through the operations service's resume, the call the API's resumeExecution makes, so the
    // two refuse the same runs with the same reasons and enqueue the same way: the run must have
    // failed or been cancelled, its saved input passes requeue's check, no resume of it is queued,
    // and the resume check over the train's declared chain allows the point; then it is queued
    // through the mediator with the run it resumes and where, replaying its decisions. The Resume
    // button resumes after the latest checkpoint (from null), a run graph node's "Resume from
    // here" at that node. Like a re-queue, it is trusted and records no actor (docs/0017 and
    // Trax.Docs/adr/0047).
    private async Task ResumeTrain(string? from)
    {
        // As for a re-queue: a click the browser sent before it applied the disabling render
        // still arrives, and each call would try to queue a resume.
        if (_metadata is null || _rerunning)
            return;

        _rerunError = null;
        _rerunning = true;
        _busy = from is null ? Busy.Resume : Busy.ResumeFromNode;
        _resumingNode = from;

        try
        {
            OperationResult result;
            using (TrustedScope.BeginTrusted("dashboard"))
                result = await OperationsService.ResumeExecutionAsync(
                    _metadata.Id,
                    from,
                    DisposalToken
                );

            if (!result.Success || result.Id is not { } entryId)
            {
                _rerunError = result.Message;
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Queued",
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"{ShortName(_metadata.Name)} has been queued to resume (ID {entryId})."
                    : $"{ShortName(_metadata.Name)} has been queued to resume. {result.Message}",
                duration: 8000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{entryId}");
        }
        catch (Exception ex)
        {
            _rerunError = ex.Message;
        }
        finally
        {
            _rerunning = false;
            _busy = Busy.None;
            _resumingNode = null;
        }
    }
}
