namespace Trax.Effect.Enums;

/// <summary>
/// Represents the resolution status of a dead-lettered job.
/// </summary>
/// <remarks>
/// When a job exceeds its retry limit, it is moved to the dead letter queue.
/// This enum tracks the lifecycle of that dead letter entry from initial
/// creation through eventual resolution.
///
/// Dead letters require manual intervention - they won't be automatically
/// retried by the scheduler. An operator must explicitly retry or acknowledge them.
/// </remarks>
public enum DeadLetterStatus
{
    /// <summary>
    /// The dead letter has not been addressed and requires manual intervention.
    /// </summary>
    /// <remarks>
    /// This is the initial status when a job is first dead-lettered.
    /// Jobs in this state should be reviewed by an operator to determine
    /// the root cause and appropriate action.
    /// </remarks>
    AwaitingIntervention = 0,

    /// <summary>
    /// The dead letter has been retried and a new execution was created.
    /// </summary>
    /// <remarks>
    /// When an operator decides to retry a dead-lettered job, a new Metadata
    /// record is created and enqueued. The DeadLetter record is marked as
    /// Retried and linked to the new Metadata via <c>RetryMetadataId</c>.
    ///
    /// Note: If the retry also fails, a NEW DeadLetter record will be created
    /// (assuming max retries are exceeded again). The original DeadLetter
    /// remains in Retried status for audit purposes.
    /// </remarks>
    Retried = 1,

    /// <summary>
    /// The dead letter has been acknowledged by an operator without retrying.
    /// </summary>
    /// <remarks>
    /// Use this status when:
    /// - The failure was expected or acceptable
    /// - The issue was resolved out-of-band (e.g., manual data fix)
    /// - The job is no longer relevant
    /// - Investigation is complete and no action is needed
    ///
    /// The <c>ResolutionNote</c> field should be populated to explain why
    /// the dead letter was acknowledged without retry.
    /// </remarks>
    Acknowledged = 2,
}
