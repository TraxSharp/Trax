using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Full detail for a single execution, including the input/output payloads and stack trace
/// that <see cref="ExecutionSummary"/> deliberately omits so paginated list reads stay lean.
/// Junction-level context is limited to <see cref="CurrentlyRunningJunction"/> while the
/// execution runs and <see cref="FailureJunction"/> once it has failed.
/// </summary>
/// <param name="Id">The execution's database id.</param>
/// <param name="ExternalId">The execution's stable external id.</param>
/// <param name="Name">The name of the train that ran.</param>
/// <param name="TrainState">The execution's current state.</param>
/// <param name="StartTime">When the execution started (UTC).</param>
/// <param name="EndTime">When the execution finished (UTC), or <c>null</c> while it is still running.</param>
/// <param name="FailureJunction">The junction that failed, for a failed execution.</param>
/// <param name="FailureReason">The failure message, for a failed execution.</param>
/// <param name="FailureException">The full name of the exception type that failed the execution.</param>
/// <param name="StackTrace">The stack trace of the failure.</param>
/// <param name="Input">The serialized train input.</param>
/// <param name="Output">The serialized train output, when the execution completed and recorded one.</param>
/// <param name="ManifestId">The manifest that scheduled the execution, or <c>null</c> for a run not started by a manifest.</param>
/// <param name="CancellationRequested">Whether cancellation has been requested for the execution.</param>
/// <param name="CurrentlyRunningJunction">The junction running now, while the execution is in progress.</param>
/// <param name="JunctionStartedAt">When <paramref name="CurrentlyRunningJunction"/> started (UTC).</param>
/// <param name="HostName">The machine name of the process that ran the execution.</param>
/// <param name="HostEnvironment">The kind of environment the process ran in (for example <c>server</c>, <c>ecs</c> or <c>lambda</c>).</param>
/// <param name="HostInstanceId">The instance-level id of the host (for example an ECS task id or a pod name).</param>
/// <param name="ChildCount">The number of executions this one started.</param>
/// <param name="FailureClass">How the failure was classified, or <c>Unclassified</c>.</param>
/// <param name="ParentId">The execution that started this one, or <c>null</c> for a top-level run.</param>
/// <param name="ScheduledTime">When the run was due, for a scheduled run; <c>null</c> otherwise.</param>
/// <param name="Executor">The project (entry assembly) of the process that ran it.</param>
/// <param name="HostLabels">The host's user-supplied labels as a JSON object, or <c>null</c>.</param>
public record ExecutionDetail(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    string? FailureJunction,
    string? FailureReason,
    string? FailureException,
    string? StackTrace,
    string? Input,
    string? Output,
    long? ManifestId,
    bool CancellationRequested,
    string? CurrentlyRunningJunction,
    DateTime? JunctionStartedAt,
    string? HostName,
    string? HostEnvironment,
    string? HostInstanceId,
    int ChildCount = 0,
    Trax.Core.Exceptions.FailureClass FailureClass = Trax.Core.Exceptions.FailureClass.Unclassified,
    long? ParentId = null,
    DateTime? ScheduledTime = null,
    string? Executor = null,
    string? HostLabels = null
);
