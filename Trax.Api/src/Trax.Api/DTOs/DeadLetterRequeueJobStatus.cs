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
