using SchedulerJob = Trax.Scheduler.Services.DeadLetterRequeue.DeadLetterRequeueJob;
using SchedulerJobStatus = Trax.Scheduler.Services.DeadLetterRequeue.DeadLetterRequeueJobStatus;

namespace Trax.Api.DTOs;

/// <summary>Where a <see cref="DeadLetterRequeueJob"/> is.</summary>
public enum DeadLetterRequeueJobStatus
{
    /// <summary>The fold is still requeueing dead letters.</summary>
    Running,

    /// <summary>The fold finished; <see cref="DeadLetterRequeueJob.Count"/> is what it requeued.</summary>
    Succeeded,

    /// <summary>The fold stopped on a server failure. The detail is in the server's log only.</summary>
    Failed,

    /// <summary>The node was shutting down, so the fold stopped between pages.</summary>
    Canceled,
}

/// <summary>
/// A requeue of every dead letter awaiting intervention, running in the background on the node
/// that started it. <c>requeueAllDeadLetters</c> returns one at once, and
/// <c>operations.deadLetters.requeueAllJob(id)</c> reads it again until <see cref="Status"/> is
/// no longer <see cref="DeadLetterRequeueJobStatus.Running"/>.
/// </summary>
/// <remarks>
/// The job runs in the scheduler's <c>IDeadLetterRequeueJobs</c>, which the dashboard's Requeue
/// All starts too; this is its GraphQL shape.
/// <para>
/// The fold commits a page of manifests at a time, and a requeued dead letter no longer awaits
/// intervention, so a fold that stops part-way (a failure, a shutdown, a restart) leaves the pages
/// it finished requeued and the rest awaiting; starting another requeues the rest.
/// </para>
/// </remarks>
/// <param name="Id">Identifies the job to <c>requeueAllJob</c> on the node that started it.</param>
/// <param name="Status">Where the job is.</param>
/// <param name="AwaitingAtStart">How many dead letters were awaiting intervention when it started.</param>
/// <param name="StartedAt">When it started (UTC).</param>
/// <param name="FinishedAt">When it stopped (UTC), or <c>null</c> while running.</param>
/// <param name="Count">How many dead letters it requeued, once it has succeeded; <c>null</c> before.</param>
/// <param name="Message">A human-readable description of where it is or how it ended.</param>
public record DeadLetterRequeueJob(
    Guid Id,
    DeadLetterRequeueJobStatus Status,
    int AwaitingAtStart,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int? Count,
    string Message
)
{
    /// <summary>
    /// <c>false</c> when a requeue-all was already running on this node, so no second one was
    /// started and this is the one already running.
    /// </summary>
    public bool Started { get; init; } = true;

    /// <summary>
    /// How many dead letters the job has requeued so far, updated as each page commits; with
    /// <see cref="AwaitingAtStart"/> it says how far a running job has got. Once it has succeeded
    /// it equals <see cref="Count"/>.
    /// </summary>
    public int Processed { get; init; }

    /// <summary>The scheduler's job as this surface returns it.</summary>
    internal static DeadLetterRequeueJob From(SchedulerJob job) =>
        new(
            job.Id,
            job.Status switch
            {
                SchedulerJobStatus.Running => DeadLetterRequeueJobStatus.Running,
                SchedulerJobStatus.Succeeded => DeadLetterRequeueJobStatus.Succeeded,
                SchedulerJobStatus.Failed => DeadLetterRequeueJobStatus.Failed,
                SchedulerJobStatus.Canceled => DeadLetterRequeueJobStatus.Canceled,
                _ => throw new ArgumentOutOfRangeException(nameof(job), job.Status, null),
            },
            job.AwaitingAtStart,
            job.StartedAt,
            job.FinishedAt,
            job.Count,
            job.Message
        )
        {
            Started = job.Started,
            Processed = job.Processed,
        };
}
