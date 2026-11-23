using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Patch input for <c>operations.updateManifest</c>. Every field is independent: a
/// <see langword="null"/> value leaves that field unchanged. Set <see cref="ClearTimeout"/> to remove
/// the per-execution timeout (since a <see langword="null"/> <see cref="TimeoutSeconds"/> means "no
/// change", not "clear").
/// </summary>
public record UpdateManifestInput(
    bool? IsEnabled = null,
    int? MaxRetries = null,
    int? Priority = null,
    int? TimeoutSeconds = null,
    bool ClearTimeout = false,
    ScheduleType? ScheduleType = null,
    string? CronExpression = null,
    int? IntervalSeconds = null
);
