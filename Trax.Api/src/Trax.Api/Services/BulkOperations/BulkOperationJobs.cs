using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// The bulk operations this node has started: <c>startBulkOperation</c>, <c>bulkOperation</c> and
/// <c>bulkOperations</c>. <see cref="StartAsync"/> snapshots the ids the filter matches, refuses
/// when their number is not the one the caller confirmed, and otherwise starts the batches in the
/// background and returns the operation at once. Only the host shutting down stops it, between
/// batches. See <c>docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each batch is at most <see cref="BatchSize"/> ids, the largest operations page (a requeue of one
/// manifest's dead letters can take up to the shared requeue's 1,000, see
/// <see cref="BulkOperationTargets.RequeueBatches"/>), run through the
/// shared operation of its kind in a scope of its own, so no transaction outlives one batch and the
/// reads the console makes meanwhile are served between them.
/// </para>
/// <para>
/// The state is in this node's memory, as the requeue-all job's is
/// (<c>docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md</c>): another node
/// does not know an operation, and a restart forgets it. One runs per node at a time; a finished
/// one is kept for <see cref="Retention"/>, and at most <see cref="MaxRetained"/> are kept.
/// </para>
/// </remarks>
internal sealed class BulkOperationJobs
{
    /// <summary>How long a finished operation can still be read.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>The most finished operations kept; the oldest is forgotten first.</summary>
    public const int MaxRetained = 100;

    /// <summary>The most ids one batch acts on: the largest page an operations read returns.</summary>
    public const int BatchSize = OperationsService.MaxPageSize;

    /// <summary>The message of an operation that stopped on a server failure.</summary>
    public const string FailedMessage =
        "The operation stopped on a server failure; the server's log has the detail. The batches "
        + "it finished stay applied; the ids it had not reached were not attempted.";

    /// <summary>The message of an operation that stopped because the node was shutting down.</summary>
    public const string CanceledMessage =
        "The operation stopped because the server was shutting down. The batches it finished stay "
        + "applied; the ids it had not reached were not attempted.";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BulkOperationJobs> _logger;
    private readonly TimeProvider _time;
    private readonly CancellationToken _stopping;
    private readonly IReadOnlyList<IBulkOperationObserver> _observers;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BulkOperationRun> _runs = [];
    private Guid? _running;

    /// <summary>
    /// Creates the node's list. Dependency injection supplies every argument; each batch resolves
    /// the shared operations from a scope of its own.
    /// </summary>
    public BulkOperationJobs(
        IServiceScopeFactory scopes,
        ILogger<BulkOperationJobs> logger,
        IEnumerable<IBulkOperationObserver> observers,
        TimeProvider? time = null,
        IHostApplicationLifetime? lifetime = null
    )
    {
        _scopes = scopes;
        _logger = logger;
        _observers = observers.ToList();
        _time = time ?? TimeProvider.System;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>The task of the operation most recently started, for tests to await.</summary>
    internal Task Current { get; private set; } = Task.CompletedTask;

    /// <summary>Test seam: runs after the snapshot, before the first batch.</summary>
    internal Func<CancellationToken, Task>? BeforeFirstBatch { get; set; }

    /// <summary>
    /// Checks the request, snapshots the ids its filter matches and, when there are as many as the
    /// caller confirmed, starts the operation and returns it. A refusal starts nothing.
    /// </summary>
    /// <param name="request">What to do, and to what.</param>
    /// <param name="actor">Who asked.</param>
    /// <param name="ct">Cancels the snapshot only; once started, the operation is not tied to it.</param>
    public async Task<StartBulkOperationResponse> StartAsync(
        BulkOperationRequest request,
        BulkOperationActor actor,
        CancellationToken ct
    )
    {
        if (Refusal(request) is { } refusal)
            return new StartBulkOperationResponse(false, refusal, null, null);
        if (RunningRefusal() is { } busy)
            return busy;

        BulkOperationSnapshot snapshot;
        await using (var scope = _scopes.CreateAsyncScope())
            snapshot = await BulkOperationTargets.SnapshotAsync(
                scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>(),
                request.Kind,
                request.Filter,
                ct
            );
        var ids = snapshot.Ids;

        var noun = BulkOperationRun.Noun(request.Kind);
        if (ids.Length != request.ExpectedCount)
            return new StartBulkOperationResponse(
                false,
                $"The filter matches {ids.Length} {noun} now, not the {request.ExpectedCount} "
                    + "confirmed, so nothing was started. Confirm the new count and start again.",
                ids.Length,
                null
            );
        if (ids.Length == 0)
            return new StartBulkOperationResponse(
                false,
                $"The filter matches no {noun}, so nothing was started.",
                0,
                null
            );

        lock (_gate)
        {
            if (_running is { } runningId)
                return Busy(_runs[runningId]);

            Prune();
            var run = new BulkOperationRun(
                Guid.NewGuid(),
                request,
                actor,
                _time.GetUtcNow().UtcDateTime,
                snapshot
            );
            _runs[run.Id] = run;
            _running = run.Id;

            // Task.Run, not the caller's context: the batches must not share the caller's lifetime.
            Current = Task.Run(() => RunAsync(run));
            return new StartBulkOperationResponse(
                true,
                $"Started: {run.Total} {noun}.",
                run.Total,
                run.Read()
            );
        }
    }

    /// <summary>The operation with this id, or <c>null</c> when this node does not know it.</summary>
    public BulkOperation? Get(Guid id)
    {
        lock (_gate)
        {
            Prune();
            return _runs.GetValueOrDefault(id)?.Read();
        }
    }

    /// <summary>Every operation this node knows, newest first.</summary>
    public IReadOnlyList<BulkOperation> List()
    {
        lock (_gate)
        {
            Prune();
            return _runs.Values.OrderByDescending(r => r.StartedAt).Select(r => r.Read()).ToList();
        }
    }

    /// <summary>The run behind an operation, for tests that read its batch timings.</summary>
    internal BulkOperationRun? Run(Guid id)
    {
        lock (_gate)
            return _runs.GetValueOrDefault(id);
    }

    private static string? Refusal(BulkOperationRequest request)
    {
        if (BulkOperationTargets.Refusal(request.Kind, request.Filter) is { } wrongList)
            return wrongList;
        if (request.ExpectedCount < 0)
            return "expectedCount is the number of matches you confirmed; it cannot be negative.";
        if (
            request.AskAfresh
            && request.Kind is not (BulkOperationKind.Requeue or BulkOperationKind.Trigger)
        )
            return "askAfresh applies only to REQUEUE and TRIGGER.";
        if (request.Kind == BulkOperationKind.Acknowledge)
        {
            if (string.IsNullOrWhiteSpace(request.Note))
                return "ACKNOWLEDGE needs a note saying why the dead letters are acknowledged.";
            if (request.Note.Length > TraxScheduler.MaxAcknowledgeNoteLength)
                return $"The note is {request.Note.Length} characters; it may be at most "
                    + $"{TraxScheduler.MaxAcknowledgeNoteLength} characters.";
        }
        else if (request.Note is not null)
            return "note applies only to ACKNOWLEDGE.";
        return null;
    }

    private StartBulkOperationResponse? RunningRefusal()
    {
        lock (_gate)
            return _running is { } runningId ? Busy(_runs[runningId]) : null;
    }

    private static StartBulkOperationResponse Busy(BulkOperationRun running) =>
        new(
            false,
            "A bulk operation is already running on this node, so this one was not started. Read "
                + "bulkOperation(id) until it finishes, then start again.",
            null,
            running.Read()
        );

    private async Task RunAsync(BulkOperationRun run)
    {
        BulkOperationStatus status;
        string? message = null;
        try
        {
            if (BeforeFirstBatch is { } seam)
                await seam(_stopping);

            var (ids, ends) = run.Plan;
            status = BulkOperationStatus.Finished;
            var offset = 0;
            foreach (var end in ends)
            {
                _stopping.ThrowIfCancellationRequested();
                var batch = ids[offset..end];
                offset = end;

                var took = Stopwatch.StartNew();
                await using var scope = _scopes.CreateAsyncScope();
                var (failures, refusal) = await BulkOperationTargets.RunAsync(
                    run,
                    batch,
                    scope.ServiceProvider,
                    _stopping
                );
                took.Stop();

                if (refusal is not null)
                {
                    status = BulkOperationStatus.Failed;
                    message =
                        $"The server refused a batch, so the operation stopped: {refusal} The "
                        + "batches before it stay applied; the ids it had not reached were not attempted.";
                    break;
                }
                run.Record(batch.Length, failures, took.Elapsed);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            status = BulkOperationStatus.Canceled;
            message = CanceledMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The bulk operation {OperationId} failed", run.Id);
            status = BulkOperationStatus.Failed;
            message = FailedMessage;
        }

        BulkOperationOutcome outcome;
        lock (_gate)
        {
            outcome = run.Finish(status, message, _time.GetUtcNow().UtcDateTime);
            _running = null;
        }

        foreach (var observer in _observers)
        {
            try
            {
                observer.Finished(outcome);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "A bulk operation observer failed for operation {OperationId}",
                    run.Id
                );
            }
        }
    }

    /// <summary>Forgets finished operations past <see cref="Retention"/>, then past <see cref="MaxRetained"/>.</summary>
    private void Prune()
    {
        var expiredBefore = _time.GetUtcNow().UtcDateTime - Retention;
        var finished = _runs
            .Values.Select(r => (r.Id, FinishedAt: r.FinishedAt))
            .Where(r => r.FinishedAt is not null)
            .OrderBy(r => r.FinishedAt)
            .ToList();

        var excess = finished.Count - MaxRetained;
        foreach (var (id, finishedAt) in finished)
        {
            if (finishedAt >= expiredBefore && excess <= 0)
                break;
            _runs.Remove(id);
            excess--;
        }
    }
}
