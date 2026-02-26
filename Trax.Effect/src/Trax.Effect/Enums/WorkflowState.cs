namespace Trax.Effect.Enums;

/// <summary>
/// Represents the possible states of a workflow during its lifecycle.
/// </summary>
/// <remarks>
/// The WorkflowState enum is used to track the current state of a workflow
/// in the metadata tracking system. It provides a standardized way to
/// represent the workflow's progress and outcome.
///
/// This enum is particularly important for:
/// 1. Filtering workflows by state in reporting and monitoring tools
/// 2. Determining which workflows need attention (e.g., failed workflows)
/// 3. Understanding the overall health of the workflow system
/// 4. Tracking the progress of long-running workflows
/// </remarks>
public enum WorkflowState
{
    /// <summary>
    /// The workflow has been created but has not yet started execution.
    /// </summary>
    /// <remarks>
    /// This is the initial state of a workflow when it is first created.
    /// Workflows in this state are waiting to be executed.
    /// </remarks>
    Pending,

    /// <summary>
    /// The workflow has successfully completed execution.
    /// </summary>
    /// <remarks>
    /// This state indicates that the workflow ran to completion without errors.
    /// The workflow's output should be available in the metadata.
    /// </remarks>
    Completed,

    /// <summary>
    /// The workflow encountered an error during execution and did not complete successfully.
    /// </summary>
    /// <remarks>
    /// This state indicates that an exception occurred during workflow execution.
    /// Details about the failure, including the exception type, message, and stack trace,
    /// should be available in the metadata.
    /// </remarks>
    Failed,

    /// <summary>
    /// The workflow is currently executing.
    /// </summary>
    /// <remarks>
    /// This state indicates that the workflow has started but has not yet completed.
    /// Workflows in this state are actively processing their steps.
    /// </remarks>
    InProgress,

    /// <summary>
    /// The workflow was explicitly cancelled by an operator or system signal.
    /// </summary>
    /// <remarks>
    /// This state indicates that the workflow was intentionally stopped before completion,
    /// either via the dashboard cancel button or a system cancellation signal.
    /// Cancelled workflows are not retried and do not create dead letters.
    /// </remarks>
    Cancelled,
}
