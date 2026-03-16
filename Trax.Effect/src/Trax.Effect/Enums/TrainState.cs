namespace Trax.Effect.Enums;

/// <summary>
/// Represents the possible states of a train during its lifecycle.
/// </summary>
/// <remarks>
/// The TrainState enum is used to track the current state of a train
/// in the metadata tracking system. It provides a standardized way to
/// represent the train's progress and outcome.
///
/// This enum is particularly important for:
/// 1. Filtering trains by state in reporting and monitoring tools
/// 2. Determining which trains need attention (e.g., failed trains)
/// 3. Understanding the overall health of the train system
/// 4. Tracking the progress of long-running trains
/// </remarks>
public enum TrainState
{
    /// <summary>
    /// The train has been created but has not yet started execution.
    /// </summary>
    /// <remarks>
    /// This is the initial state of a train when it is first created.
    /// Trains in this state are waiting to be executed.
    /// </remarks>
    Pending,

    /// <summary>
    /// The train has successfully completed execution.
    /// </summary>
    /// <remarks>
    /// This state indicates that the train ran to completion without errors.
    /// The train's output should be available in the metadata.
    /// </remarks>
    Completed,

    /// <summary>
    /// The train encountered an error during execution and did not complete successfully.
    /// </summary>
    /// <remarks>
    /// This state indicates that an exception occurred during train execution.
    /// Details about the failure, including the exception type, message, and stack trace,
    /// should be available in the metadata.
    /// </remarks>
    Failed,

    /// <summary>
    /// The train is currently executing.
    /// </summary>
    /// <remarks>
    /// This state indicates that the train has started but has not yet completed.
    /// Trains in this state are actively processing their junctions.
    /// </remarks>
    InProgress,

    /// <summary>
    /// The train was explicitly cancelled by an operator or system signal.
    /// </summary>
    /// <remarks>
    /// This state indicates that the train was intentionally stopped before completion,
    /// either via the dashboard cancel button or a system cancellation signal.
    /// Cancelled trains are not retried and do not create dead letters.
    /// </remarks>
    Cancelled,
}
