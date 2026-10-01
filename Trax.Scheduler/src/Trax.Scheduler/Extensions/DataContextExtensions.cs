using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for <see cref="IDataContext"/> used by the scheduler.
/// </summary>
/// <remarks>
/// Seeding runs at every host start. The schedule, input, train and the options code always owns
/// are written each time; the manifest's enabled flag and the group's settings, which operators
/// change at runtime, are written only when the options state them.
/// </remarks>
internal static class DataContextExtensions
{
    /// <summary>
    /// Ensures a ManifestGroup exists with the given name, creating one if necessary. An existing
    /// group's settings change only where <paramref name="group"/> states them.
    /// </summary>
    /// <returns>The ManifestGroup ID.</returns>
    public static async Task<long> EnsureManifestGroupAsync(
        this IDataContext context,
        string groupName,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
    {
        var existing = await context.ManifestGroups.FirstOrDefaultAsync(
            g => g.Name == groupName,
            ct
        );

        if (existing != null)
        {
            var changed = false;
            if (group.Priority is { } priority && existing.Priority != priority)
            {
                existing.Priority = priority;
                changed = true;
            }
            if (group.MaxActiveJobsStated && existing.MaxActiveJobs != group.MaxActiveJobs)
            {
                existing.MaxActiveJobs = group.MaxActiveJobs;
                changed = true;
            }
            if (group.IsEnabled is { } enabled && existing.IsEnabled != enabled)
            {
                existing.IsEnabled = enabled;
                changed = true;
            }
            if (changed)
                existing.UpdatedAt = DateTime.UtcNow;
            return existing.Id;
        }

        var created = new ManifestGroup
        {
            Name = groupName,
            Priority = group.Priority ?? group.PriorityIfNew,
            MaxActiveJobs = group.MaxActiveJobsStated ? group.MaxActiveJobs : null,
            IsEnabled = group.IsEnabled ?? true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.ManifestGroups.Add(created);
        await context.SaveChanges(ct);

        return created.Id;
    }

    /// <summary>
    /// Creates or updates a manifest with the specified configuration.
    /// </summary>
    public static Task<Manifest> UpsertManifestAsync<TTrain, TInput, TOutput>(
        this IDataContext context,
        string externalId,
        TInput input,
        Schedule schedule,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties =>
        context.UpsertManifestAsync(
            typeof(TTrain),
            externalId,
            input,
            schedule,
            options,
            groupId,
            group,
            ct
        );

    /// <summary>
    /// Non-generic overload that accepts train type as a <see cref="Type"/> parameter.
    /// </summary>
    internal static async Task<Manifest> UpsertManifestAsync(
        this IDataContext context,
        Type trainType,
        string externalId,
        IManifestProperties input,
        Schedule schedule,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(groupId, group, ct);

        var existing = await context.Manifests.FirstOrDefaultAsync(
            m => m.ExternalId == externalId,
            ct
        );

        if (existing != null)
        {
            // Update only scheduling-related fields, preserve runtime state
            existing.Name = trainType.FullName!;
            existing.SetProperties(input);
            if (options._isEnabled is { } isEnabled)
                existing.IsEnabled = isEnabled;
            ApplyStatedSettings(existing, options);
            existing.ManifestGroupId = manifestGroupId;
            ApplySchedule(existing, schedule);
            ApplyVariance(existing, schedule, options);
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);
            ApplyFailureWindow(existing, options);

            return existing;
        }

        // Create new manifest
        var manifest = new Manifest
        {
            ExternalId = externalId,
            Name = trainType.FullName!,
            IsEnabled = options.IsEnabled,
            MaxRetries = options.MaxRetries,
            TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null,
            ManifestGroupId = manifestGroupId,
            Priority = options.Priority,
        };
        manifest.SetProperties(input);
        ApplySchedule(manifest, schedule);
        ApplyVariance(manifest, schedule, options);
        ApplyMisfireOptions(manifest, options);
        ApplyExclusions(manifest, options);
        ApplyFailureWindow(manifest, options);

        context.Manifests.Add(manifest);

        return manifest;
    }

    /// <summary>
    /// Creates or updates a dependent manifest that triggers after a parent manifest succeeds.
    /// </summary>
    public static Task<Manifest> UpsertDependentManifestAsync<TTrain, TInput, TOutput>(
        this IDataContext context,
        string externalId,
        TInput input,
        long dependsOnManifestId,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties =>
        context.UpsertDependentManifestAsync(
            typeof(TTrain),
            externalId,
            input,
            dependsOnManifestId,
            options,
            groupId,
            group,
            ct
        );

    /// <summary>
    /// Non-generic overload that accepts train type as a <see cref="Type"/> parameter.
    /// </summary>
    internal static async Task<Manifest> UpsertDependentManifestAsync(
        this IDataContext context,
        Type trainType,
        string externalId,
        IManifestProperties input,
        long dependsOnManifestId,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(groupId, group, ct);

        var existing = await context.Manifests.FirstOrDefaultAsync(
            m => m.ExternalId == externalId,
            ct
        );

        var scheduleType = options.IsDormant
            ? ScheduleType.DormantDependent
            : ScheduleType.Dependent;

        if (existing != null)
        {
            existing.Name = trainType.FullName!;
            existing.SetProperties(input);
            if (options._isEnabled is { } isEnabled)
                existing.IsEnabled = isEnabled;
            ApplyStatedSettings(existing, options);
            existing.ManifestGroupId = manifestGroupId;
            existing.ScheduleType = scheduleType;
            existing.DependsOnManifestId = dependsOnManifestId;
            existing.CronExpression = null;
            existing.IntervalSeconds = null;
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);
            ApplyFailureWindow(existing, options);

            return existing;
        }

        var manifest = new Manifest
        {
            ExternalId = externalId,
            Name = trainType.FullName!,
            IsEnabled = options.IsEnabled,
            MaxRetries = options.MaxRetries,
            TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null,
            ManifestGroupId = manifestGroupId,
            Priority = options.Priority,
            ScheduleType = scheduleType,
            DependsOnManifestId = dependsOnManifestId,
        };
        manifest.SetProperties(input);
        ApplyMisfireOptions(manifest, options);
        ApplyExclusions(manifest, options);
        ApplyFailureWindow(manifest, options);

        context.Manifests.Add(manifest);

        return manifest;
    }

    /// <summary>
    /// Creates or updates a one-off manifest that fires once at the specified time, then auto-disables.
    /// </summary>
    public static Task<Manifest> UpsertOnceManifestAsync<TTrain, TInput, TOutput>(
        this IDataContext context,
        string externalId,
        TInput input,
        DateTime scheduledAt,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties =>
        context.UpsertOnceManifestAsync(
            typeof(TTrain),
            externalId,
            input,
            scheduledAt,
            options,
            groupId,
            group,
            ct
        );

    /// <summary>
    /// Non-generic overload that accepts train type as a <see cref="Type"/> parameter.
    /// </summary>
    internal static async Task<Manifest> UpsertOnceManifestAsync(
        this IDataContext context,
        Type trainType,
        string externalId,
        IManifestProperties input,
        DateTime scheduledAt,
        ManifestOptions options,
        string groupId,
        ManifestGroupSeed group,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(groupId, group, ct);

        var existing = await context.Manifests.FirstOrDefaultAsync(
            m => m.ExternalId == externalId,
            ct
        );

        if (existing != null)
        {
            existing.Name = trainType.FullName!;
            existing.SetProperties(input);
            if (options._isEnabled is { } isEnabled)
                existing.IsEnabled = isEnabled;
            ApplyStatedSettings(existing, options);
            existing.ManifestGroupId = manifestGroupId;
            existing.ScheduleType = ScheduleType.Once;
            existing.ScheduledAt = scheduledAt;
            existing.CronExpression = null;
            existing.IntervalSeconds = null;
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);
            ApplyFailureWindow(existing, options);

            return existing;
        }

        var manifest = new Manifest
        {
            ExternalId = externalId,
            Name = trainType.FullName!,
            IsEnabled = options.IsEnabled,
            MaxRetries = options.MaxRetries,
            TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null,
            ManifestGroupId = manifestGroupId,
            Priority = options.Priority,
            ScheduleType = ScheduleType.Once,
            ScheduledAt = scheduledAt,
        };
        manifest.SetProperties(input);
        ApplyMisfireOptions(manifest, options);
        ApplyExclusions(manifest, options);
        ApplyFailureWindow(manifest, options);

        context.Manifests.Add(manifest);

        return manifest;
    }

    /// <summary>
    /// Writes the retries, timeout and priority to an existing manifest, each only when the code
    /// states it. An operator can change any of them at runtime, and an unstated one keeps
    /// whatever the database holds (scheduler/0011).
    /// </summary>
    private static void ApplyStatedSettings(Manifest existing, ManifestOptions options)
    {
        if (options._maxRetries is { } maxRetries)
            existing.MaxRetries = maxRetries;
        if (options._timeoutStated)
            existing.TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null;
        if (options._priority is { } priority)
            existing.Priority = priority;
    }

    /// <summary>
    /// Applies schedule configuration to a manifest.
    /// </summary>
    private static void ApplySchedule(Manifest manifest, Schedule schedule)
    {
        var unchangedCron =
            manifest.ScheduleType == ScheduleType.Cron
            && schedule.Type == ScheduleType.Cron
            && manifest.CronExpression == schedule.CronExpression;
        var firstOccurrence = manifest.NextScheduledRun;

        manifest.ScheduleType = schedule.Type;
        manifest.CronExpression = schedule.CronExpression;
        manifest.IntervalSeconds = schedule.Interval.HasValue
            ? (int)schedule.Interval.Value.TotalSeconds
            : null;

        // Clear pre-computed next run on schedule change so it gets recomputed
        // after the next successful execution.
        manifest.NextScheduledRun = null;

        // A cron that has never succeeded first runs at its first occurrence after it was
        // scheduled, not on the next poll. Re-stating the same cron keeps the occurrence already
        // recorded, so a restart does not push a pending first run back.
        if (schedule.Type == ScheduleType.Cron && manifest.LastSuccessfulRun is null)
            manifest.NextScheduledRun =
                unchangedCron && firstOccurrence is not null
                    ? firstOccurrence
                    : CronParser
                        .Parse(schedule.CronExpression!)
                        .GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Applies variance configuration to a manifest, merging from both Schedule and ManifestOptions.
    /// Schedule.Variance takes precedence over ManifestOptions.Variance.
    /// </summary>
    private static void ApplyVariance(
        Manifest manifest,
        Schedule? schedule,
        ManifestOptions options
    )
    {
        var variance = schedule?.Variance ?? options.Variance;
        if (variance is null)
        {
            manifest.VarianceSeconds = null;
            return;
        }

        if (variance.Value < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Schedule variance must be non-negative. "
                    + $"Got: {variance.Value}. "
                    + "Use a positive TimeSpan for the maximum random delay.\n\n"
                    + "  .Variance(TimeSpan.FromMinutes(2)) // correct\n"
            );
        }

        if (manifest.ScheduleType is not ScheduleType.Interval and not ScheduleType.Cron)
        {
            throw new InvalidOperationException(
                "Schedule variance is only supported for Interval and Cron schedule types. "
                    + $"Manifest has ScheduleType={manifest.ScheduleType}.\n\n"
                    + "Remove the .Variance() call or change the schedule type to Interval or Cron.\n"
            );
        }

        manifest.VarianceSeconds = (int)variance.Value.TotalSeconds;
    }

    /// <summary>
    /// Applies misfire policy configuration to a manifest from the options.
    /// </summary>
    private static void ApplyMisfireOptions(Manifest manifest, ManifestOptions options)
    {
        if (options.MisfirePolicy.HasValue)
            manifest.MisfirePolicy = options.MisfirePolicy.Value;

        manifest.MisfireThresholdSeconds = options.MisfireThreshold.HasValue
            ? (int)options.MisfireThreshold.Value.TotalSeconds
            : null;
    }

    /// <summary>
    /// Writes the manifest's own failure window when the options state one. Unstated, a new
    /// manifest keeps null (the scheduler's window) and an existing one keeps the window it has.
    /// </summary>
    private static void ApplyFailureWindow(Manifest manifest, ManifestOptions options)
    {
        if (options.FailureWindow is { } window)
            manifest.FailureWindowSeconds = (int)window.TotalSeconds;
    }

    /// <summary>
    /// Applies exclusion window configuration to a manifest from the options.
    /// </summary>
    private static void ApplyExclusions(Manifest manifest, ManifestOptions options)
    {
        manifest.SetExclusions(options.Exclusions);
    }
}

/// <summary>
/// The group settings one seeding call states. A null field (or an unstated limit) leaves an
/// existing group's value alone.
/// </summary>
/// <param name="Priority">The group priority the options state, or null.</param>
/// <param name="MaxActiveJobsStated">Whether the options state the limit; its value may be null (no limit).</param>
/// <param name="MaxActiveJobs">The stated limit, when <paramref name="MaxActiveJobsStated"/>.</param>
/// <param name="IsEnabled">The enabled flag the options state, or null.</param>
/// <param name="PriorityIfNew">The priority a new group takes when none is stated.</param>
internal readonly record struct ManifestGroupSeed(
    int? Priority = null,
    bool MaxActiveJobsStated = false,
    int? MaxActiveJobs = null,
    bool? IsEnabled = null,
    int PriorityIfNew = 0
);
