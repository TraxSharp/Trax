using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// One execution (a single run of a train) as the list and point reads return it. Carries no
/// input, output or stack trace: those are on <see cref="ExecutionDetail"/>, which the
/// single-execution detail read returns.
/// </summary>
/// <param name="Id">The execution's id.</param>
/// <param name="ExternalId">The execution's stable external id.</param>
/// <param name="Name">The name of the train that ran (the train interface's full name).</param>
/// <param name="TrainState">The execution's current state.</param>
/// <param name="StartTime">When the execution started (UTC).</param>
/// <param name="EndTime">When the execution finished (UTC), or <c>null</c> while it is still running.</param>
/// <param name="FailureJunction">The junction that failed, for a failed execution.</param>
/// <param name="FailureReason">The failure message, for a failed execution.</param>
/// <param name="ManifestId">The manifest that scheduled the execution, or <c>null</c> for a run not started by a manifest.</param>
/// <param name="CancellationRequested">Whether cancellation has been requested for the execution.</param>
/// <param name="HostName">The machine name of the process that ran the execution, if recorded.</param>
/// <param name="HostEnvironment">The kind of environment that process ran in (for example <c>server</c>, <c>ecs</c> or <c>lambda</c>), if recorded.</param>
/// <param name="HostInstanceId">The instance-level id of that process (for example an ECS task id or a pod name), if recorded.</param>
/// <param name="FailureClass">How the failure was classified, or <c>Unclassified</c>.</param>
public record ExecutionSummary(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    string? FailureJunction,
    string? FailureReason,
    long? ManifestId,
    bool CancellationRequested,
    string? HostName = null,
    string? HostEnvironment = null,
    string? HostInstanceId = null,
    Trax.Core.Exceptions.FailureClass FailureClass = Trax.Core.Exceptions.FailureClass.Unclassified
)
{
    /// <summary>
    /// The execution that started this one, for a run started from inside another train's run;
    /// <c>null</c> otherwise. <c>executionChildren</c> lists a parent's runs the other way.
    /// </summary>
    public long? ParentId { get; init; }

    /// <summary>
    /// The junction the execution is running now, while it is in progress; <c>null</c> otherwise.
    /// </summary>
    public string? CurrentlyRunningJunction { get; init; }
}
