using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Models;

// The rows the server-paged grids show. Each is a projection of its entity without the columns
// no grid displays: a run's input, output and stack trace, a work queue entry's input, a
// manifest's properties and exclusions. (A log row is read through the operations service, whose
// entries carry a stack trace capped at 4,000 characters.) Those are text columns that can hold
// megabytes each, and a grid re-reads its page on every poll tick, so loading whole entities
// moved them over the wire and into circuit memory every few seconds for nothing. The detail
// pages still load the whole row. Grid filters and sorts are applied after the projection, so a
// column's Property names a member of the row type and EF Core translates it to the column.

/// <summary>A run (metadata row) as the run grids show it.</summary>
internal sealed class RunRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string ExternalId { get; init; } = "";
    public TrainState TrainState { get; init; }
    public string? CurrentlyRunningJunction { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string? FailureJunction { get; init; }
    public string? FailureReason { get; init; }
    public long? ParentId { get; init; }
    public long? ManifestId { get; init; }
    public string? HostEnvironment { get; init; }
    public string? HostName { get; init; }

    public static readonly Expression<Func<Metadata, RunRow>> Projection = m => new RunRow
    {
        Id = m.Id,
        Name = m.Name,
        ExternalId = m.ExternalId,
        TrainState = m.TrainState,
        CurrentlyRunningJunction = m.CurrentlyRunningJunction,
        StartTime = m.StartTime,
        EndTime = m.EndTime,
        FailureJunction = m.FailureJunction,
        FailureReason = m.FailureReason,
        ParentId = m.ParentId,
        ManifestId = m.ManifestId,
        HostEnvironment = m.HostEnvironment,
        HostName = m.HostName,
    };
}

/// <summary>A work queue entry as the work queue grid shows it.</summary>
internal sealed class WorkQueueRow
{
    public long Id { get; init; }
    public string TrainName { get; init; } = "";
    public WorkQueueStatus Status { get; init; }
    public int Priority { get; init; }
    public long? ManifestId { get; init; }
    public long? MetadataId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DispatchedAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public string? SubjectKey { get; init; }

    public static readonly Expression<Func<WorkQueue, WorkQueueRow>> Projection =
        q => new WorkQueueRow
        {
            Id = q.Id,
            TrainName = q.TrainName,
            Status = q.Status,
            Priority = q.Priority,
            ManifestId = q.ManifestId,
            MetadataId = q.MetadataId,
            CreatedAt = q.CreatedAt,
            DispatchedAt = q.DispatchedAt,
            ConfirmedAt = q.ConfirmedAt,
            SubjectKey = q.SubjectKey,
        };
}

/// <summary>A manifest as the manifest grids show it, with its group's name.</summary>
internal sealed class ManifestRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string ExternalId { get; init; } = "";
    public string? GroupName { get; init; }
    public bool IsEnabled { get; init; }
    public ScheduleType ScheduleType { get; init; }
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public int MaxRetries { get; init; }
    public int? TimeoutSeconds { get; init; }
    public DateTime? LastSuccessfulRun { get; init; }

    public static readonly Expression<Func<Manifest, ManifestRow>> Projection = m => new ManifestRow
    {
        Id = m.Id,
        Name = m.Name,
        ExternalId = m.ExternalId,
        GroupName = m.ManifestGroup.Name,
        IsEnabled = m.IsEnabled,
        ScheduleType = m.ScheduleType,
        CronExpression = m.CronExpression,
        IntervalSeconds = m.IntervalSeconds,
        MaxRetries = m.MaxRetries,
        TimeoutSeconds = m.TimeoutSeconds,
        LastSuccessfulRun = m.LastSuccessfulRun,
    };
}

/// <summary>
/// A log entry as the log grids show it, read through <c>IOperationsService.GetLogsAsync</c>
/// rather than projected here, without the stack trace the grids do not show.
/// </summary>
internal sealed class LogRow
{
    public long Id { get; init; }
    public long MetadataId { get; init; }
    public LogLevel Level { get; init; }
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Exception { get; init; }

    public static LogRow From(LogRecord log) =>
        new()
        {
            Id = log.Id,
            MetadataId = log.MetadataId,
            Level = log.Level,
            Category = log.Category,
            Message = log.Message,
            Exception = log.Exception,
        };
}

/// <summary>
/// A state-machine instance as the State machines grid shows it, read through
/// <c>IOperationsService.GetMachineInstancesAsync</c>. It has no context and no owner key,
/// because the operations service never reads either for an operator.
/// </summary>
internal sealed class MachineInstanceRow
{
    public long RowId { get; init; }
    public string Machine { get; init; } = "";
    public SnapshotOwnerKind OwnerKind { get; init; }
    public Guid Id { get; init; }
    public string State { get; init; } = "";
    public int Version { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool HasLiveInvokedRun { get; init; }

    public static MachineInstanceRow From(MachineInstanceRecord instance) =>
        new()
        {
            RowId = instance.RowId,
            Machine = instance.Machine,
            OwnerKind = instance.OwnerKind,
            Id = instance.Id,
            State = instance.State,
            Version = instance.Version,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            HasLiveInvokedRun = instance.HasLiveInvokedRun,
        };
}
