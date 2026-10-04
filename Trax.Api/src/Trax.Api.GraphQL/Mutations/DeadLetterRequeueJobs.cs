using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// The requeue-all jobs this node has started. <c>requeueAllDeadLetters</c> starts the fold here
/// and returns its handle at once, so the fold is never bounded by the request: not by
/// HotChocolate's execution timeout, not by the client going away. Only the node shutting down
/// stops it, between pages. See
/// <c>docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md</c>.
/// </summary>
/// <remarks>
/// The state is in this node's memory: another node does not know the job, and a restart forgets
/// it. Nothing is lost by that, because the fold is resumable (see <see cref="DeadLetterRequeueJob"/>).
/// One fold runs per node at a time; asking again while one runs returns that one. A finished job
/// is kept for <see cref="Retention"/>, and at most <see cref="MaxRetained"/> are kept.
/// </remarks>
public sealed class DeadLetterRequeueJobs
{
    /// <summary>How long a finished job can still be read.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>The most finished jobs kept; the oldest is forgotten first.</summary>
    internal const int MaxRetained = 100;

    internal const string FailedMessage =
        "The requeue stopped on a server failure; the server's log has the detail. Dead letters "
        + "it requeued stay requeued; requeueAllDeadLetters again requeues the rest.";

    internal static string OtherModeMessage(bool runningAsksAfresh) =>
        $"A requeue-all that {(runningAsksAfresh ? "asks its deciders afresh" : "replays recorded decisions")} "
        + "is already running on this node, so this one was not started. Read requeueAllJob(id) "
        + "until it finishes, then ask again.";

    internal const string CanceledMessage =
        "The requeue stopped because the server was shutting down. Dead letters it requeued stay "
        + "requeued; requeueAllDeadLetters again requeues the rest.";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DeadLetterRequeueJobs> _logger;
    private readonly TimeProvider _time;
    private readonly CancellationToken _stopping;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DeadLetterRequeueJob> _jobs = [];
    private Guid? _running;
    private bool _runningAsksAfresh;

    internal DeadLetterRequeueJobs(
        IServiceScopeFactory scopes,
        ILogger<DeadLetterRequeueJobs> logger,
        TimeProvider time,
        CancellationToken stopping
    )
    {
        _scopes = scopes;
        _logger = logger;
        _time = time;
        _stopping = stopping;
    }

    /// <summary>The task of the fold most recently started, for tests to await.</summary>
    internal Task Current { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts a requeue-all fold in the background and returns its handle, or returns the one
    /// already running on this node. <paramref name="askAfresh"/> makes every new run ask its
    /// deciders again rather than replay. A request in the other mode than the running fold is
    /// not folded into it: it gets the running job back with a message saying nothing was started.
    /// </summary>
    internal DeadLetterRequeueJob Start(int awaitingAtStart, bool askAfresh = false)
    {
        lock (_gate)
        {
            if (_running is { } runningId)
                return _runningAsksAfresh == askAfresh
                    ? _jobs[runningId] with
                    {
                        Started = false,
                    }
                    : _jobs[runningId] with
                    {
                        Started = false,
                        Message = OtherModeMessage(_runningAsksAfresh),
                    };

            Prune();

            var job = new DeadLetterRequeueJob(
                Guid.NewGuid(),
                DeadLetterRequeueJobStatus.Running,
                awaitingAtStart,
                _time.GetUtcNow().UtcDateTime,
                FinishedAt: null,
                Count: null,
                Message: $"Requeueing {awaitingAtStart} dead letter(s) awaiting intervention."
            );
            _jobs[job.Id] = job;
            _running = job.Id;
            _runningAsksAfresh = askAfresh;

            // Task.Run, not the caller's context: the fold must not share the request's lifetime.
            Current = Task.Run(() => RunAsync(job.Id, askAfresh));
            return job;
        }
    }

    /// <summary>The job with this id, or <c>null</c> when this node does not know it.</summary>
    internal DeadLetterRequeueJob? Get(Guid id)
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
            var result = askAfresh
                ? await scheduler.RequeueAllDeadLettersAsync(askAfresh: true, _stopping)
                : await scheduler.RequeueAllDeadLettersAsync(_stopping);
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
                Message = message,
                FinishedAt = _time.GetUtcNow().UtcDateTime,
            };
            _running = null;
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
