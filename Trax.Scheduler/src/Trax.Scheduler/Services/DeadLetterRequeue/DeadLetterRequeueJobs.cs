using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Services.DeadLetterRequeue;

/// <summary>
/// The requeue-all jobs this node has started. <see cref="StartAsync"/> starts the fold here and
/// returns its handle at once, so the fold is never bounded by its caller: not by a GraphQL
/// request's execution timeout, not by a dashboard circuit, not by the client going away. Only
/// the node shutting down stops it, between pages.
/// </summary>
/// <remarks>
/// The state is in this node's memory: another node does not know the job, and a restart forgets
/// it. Nothing is lost by that, because the fold is resumable (see <see cref="DeadLetterRequeueJob"/>).
/// One fold runs per node at a time; asking again while one runs returns that one. A finished job
/// is kept for <see cref="Retention"/>, and at most <see cref="MaxRetained"/> are kept.
/// </remarks>
public sealed class DeadLetterRequeueJobs : IDeadLetterRequeueJobs
{
    /// <summary>How long a finished job can still be read.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>The most finished jobs kept; the oldest is forgotten first.</summary>
    public const int MaxRetained = 100;

    /// <summary>The message of a job that stopped on a server failure.</summary>
    public const string FailedMessage =
        "The requeue stopped on a server failure; the server's log has the detail. Dead letters "
        + "it requeued stay requeued; requeueAllDeadLetters again requeues the rest.";

    /// <summary>The message of a job that stopped because the node was shutting down.</summary>
    public const string CanceledMessage =
        "The requeue stopped because the server was shutting down. Dead letters it requeued stay "
        + "requeued; requeueAllDeadLetters again requeues the rest.";

    /// <summary>
    /// The message returned with the running job to a request in the other mode, which started
    /// nothing.
    /// </summary>
    /// <param name="runningAsksAfresh">Whether the running job asks its deciders afresh.</param>
    public static string OtherModeMessage(bool runningAsksAfresh) =>
        $"A requeue-all that {(runningAsksAfresh ? "asks its deciders afresh" : "replays recorded decisions")} "
        + "is already running on this node, so this one was not started. Read requeueAllJob(id) "
        + "until it finishes, then ask again.";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DeadLetterRequeueJobs> _logger;
    private readonly TimeProvider _time;
    private readonly CancellationToken _stopping;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DeadLetterRequeueJob> _jobs = [];
    private Guid? _running;

    /// <summary>
    /// Creates the node's job list. Dependency injection supplies every argument; the fold resolves
    /// <see cref="ITraxScheduler"/> and <see cref="IDataContextProviderFactory"/> from a scope of
    /// its own.
    /// </summary>
    /// <param name="scopes">Creates the scope each fold and each count runs in.</param>
    /// <param name="logger">Records a fold's failure, which its message does not carry.</param>
    /// <param name="time">The clock for start, finish and retention; the system clock when omitted.</param>
    /// <param name="lifetime">
    /// The host whose shutdown stops a running fold. Without one, nothing stops a fold early.
    /// </param>
    public DeadLetterRequeueJobs(
        IServiceScopeFactory scopes,
        ILogger<DeadLetterRequeueJobs> logger,
        TimeProvider? time = null,
        IHostApplicationLifetime? lifetime = null
    )
    {
        _scopes = scopes;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>The task of the fold most recently started, for tests to await.</summary>
    internal Task Current { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public async Task<DeadLetterRequeueJob> StartAsync(
        bool askAfresh = false,
        CancellationToken ct = default
    )
    {
        int awaiting;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
            using var db = await factory.CreateDbContextAsync(ct);
            awaiting = await db.DeadLetters.CountAsync(
                d => d.Status == DeadLetterStatus.AwaitingIntervention,
                ct
            );
        }

        return Start(awaiting, askAfresh);
    }

    /// <summary>
    /// Starts a fold that reports <paramref name="awaitingAtStart"/>, or returns the one already
    /// running on this node. A request in the other mode than the running fold is not folded into
    /// it: it gets the running job back with a message saying nothing was started.
    /// </summary>
    internal DeadLetterRequeueJob Start(int awaitingAtStart, bool askAfresh = false)
    {
        lock (_gate)
        {
            if (_running is { } runningId)
            {
                var running = _jobs[runningId];
                return running.AskAfresh == askAfresh
                    ? running with
                    {
                        Started = false,
                    }
                    : running with
                    {
                        Started = false,
                        Message = OtherModeMessage(running.AskAfresh),
                    };
            }

            Prune();

            var job = new DeadLetterRequeueJob(
                Guid.NewGuid(),
                DeadLetterRequeueJobStatus.Running,
                awaitingAtStart,
                _time.GetUtcNow().UtcDateTime,
                FinishedAt: null,
                Count: null,
                Message: $"Requeueing {awaitingAtStart} dead letter(s) awaiting intervention.",
                AskAfresh: askAfresh
            );
            _jobs[job.Id] = job;
            _running = job.Id;

            // Task.Run, not the caller's context: the fold must not share the caller's lifetime.
            Current = Task.Run(() => RunAsync(job.Id, askAfresh));
            return job;
        }
    }

    /// <inheritdoc />
    public DeadLetterRequeueJob? Get(Guid id)
    {
        lock (_gate)
        {
            Prune();
            return _jobs.GetValueOrDefault(id);
        }
    }

    private async Task RunAsync(Guid id, bool askAfresh)
    {
        DeadLetterRequeueJobStatus status;
        int? count = null;
        string message;

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
            // Each page's running total is written onto the job as it commits, so a reader sees
            // how far the fold has got. A scheduler that predates the progress overload runs the
            // fold without reporting, and the job shows its count only when it finishes.
            var result = await scheduler.RequeueAllDeadLettersAsync(
                askAfresh,
                new JobProgress(this, id),
                _stopping
            );
            status = DeadLetterRequeueJobStatus.Succeeded;
            count = result.Count;
            message = result.Message;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            status = DeadLetterRequeueJobStatus.Canceled;
            message = CanceledMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The requeue-all job {JobId} failed", id);
            status = DeadLetterRequeueJobStatus.Failed;
            message = FailedMessage;
        }

        lock (_gate)
        {
            _jobs[id] = _jobs[id] with
            {
                Status = status,
                Count = count,
                Processed = count ?? _jobs[id].Processed,
                Message = message,
                FinishedAt = _time.GetUtcNow().UtcDateTime,
            };
            _running = null;
        }
    }

    /// <summary>Writes a fold's running total onto its job, synchronously on the fold's thread.</summary>
    private sealed class JobProgress(DeadLetterRequeueJobs jobs, Guid id) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (jobs._gate)
            {
                if (jobs._jobs.TryGetValue(id, out var job))
                    jobs._jobs[id] = job with
                    {
                        Processed = value,
                        Message =
                            $"Requeued {value} of {job.AwaitingAtStart} dead letter(s) awaiting "
                            + "intervention so far.",
                    };
            }
        }
    }

    /// <summary>Forgets finished jobs past <see cref="Retention"/>, then past <see cref="MaxRetained"/>.</summary>
    private void Prune()
    {
        var expiredBefore = _time.GetUtcNow().UtcDateTime - Retention;
        var finished = _jobs
            .Values.Where(j => j.FinishedAt is not null)
            .Select(j => (j.Id, FinishedAt: j.FinishedAt!.Value))
            .OrderBy(j => j.FinishedAt)
            .ToList();

        var excess = finished.Count - MaxRetained;
        foreach (var (id, finishedAt) in finished)
        {
            if (finishedAt >= expiredBefore && excess <= 0)
                break;
            _jobs.Remove(id);
            excess--;
        }
    }
}
