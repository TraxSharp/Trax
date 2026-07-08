using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Full detail for a single execution, including the input/output payloads and stack trace
/// that <see cref="ExecutionSummary"/> deliberately omits so paginated list reads stay lean.
/// Backed by a single <c>trax.metadata</c> row; there is no separate junction table, so
/// junction context is the <see cref="CurrentlyRunningJunction"/> / <see cref="FailureJunction"/>
/// fields the framework records on the metadata itself.
/// </summary>
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
    int ChildCount = 0
);
