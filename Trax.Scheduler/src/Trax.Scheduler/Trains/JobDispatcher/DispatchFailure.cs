using Trax.Core.Exceptions;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// How the dispatcher records a job it failed to deliver, and how long it waits before trying
/// again.
/// </summary>
/// <remarks>
/// Every dispatch attempt has its own run, and an attempt that fails before any runner started
/// the job is recorded <c>Failed</c> on that run. While the entry still has attempts left it is
/// requeued, and its run carries <see cref="Requeued"/> as its <c>FailureException</c>: the job
/// has not failed, only one delivery of it has, so that run does not count toward the manifest's
/// retries or dead letter. The attempt that exhausts
/// <see cref="Configuration.SchedulerConfiguration.MaxDispatchAttempts"/> records the submitter's
/// exception as usual and counts once.
/// </remarks>
internal static class DispatchFailure
{
    /// <summary>
    /// The <c>FailureException</c> of a failed dispatch attempt whose entry was requeued. A query
    /// that counts a manifest's failures leaves these rows out.
    /// </summary>
    internal const string Requeued = "DispatchRequeued";

    /// <summary>
    /// The <c>FailureException</c> a lifecycle event carries for a run that failed at dispatch.
    /// Not <c>TrainException</c>, so a subscriber outside the operations view is shown a masked
    /// reason, as for any failure that is not a train's own.
    /// </summary>
    internal const string Failed = "DispatchFailed";

    /// <summary>
    /// The <c>FailureReason</c> a lifecycle event carries for a run that failed at dispatch.
    /// </summary>
    internal const string FailedReason =
        "The run could not be dispatched, so it never started. See the run's record for the cause.";

    /// <summary>
    /// The failure a lifecycle event is given for <paramref name="run"/>, which failed at dispatch:
    /// <see cref="Failed"/> with <see cref="FailedReason"/> in place of the dispatcher's own detail,
    /// which stays on the run's row. Its failure class and junction are kept: Trax produces those.
    /// </summary>
    internal static Exception Published(Trax.Effect.Models.Metadata.Metadata run)
    {
        var exception = new TrainException(FailedReason);
        exception.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = run.Name,
            TrainExternalId = run.ExternalId,
            Type = Failed,
            Junction = run.FailureJunction ?? "DispatchJobsJunction",
            Message = FailedReason,
            FailureClass = run.FailureClass,
        };
        return exception;
    }

    /// <summary>The wait before the first retry of a failed dispatch.</summary>
    internal static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(5);

    /// <summary>The longest wait between dispatch attempts.</summary>
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a requeued entry waits after its <paramref name="attempts"/>th failed dispatch:
    /// <see cref="FirstBackoff"/>, doubling with each attempt, capped at <see cref="MaxBackoff"/>.
    /// </summary>
    internal static TimeSpan Backoff(int attempts)
    {
        var doublings = Math.Clamp(attempts - 1, 0, 16);
        var backoff = FirstBackoff * Math.Pow(2, doublings);
        return backoff < MaxBackoff ? backoff : MaxBackoff;
    }
}
