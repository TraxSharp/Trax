using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Configuration;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for <see cref="IDataContext"/> used by the scheduler.
/// </summary>
public static class DataContextExtensions
{
    /// <summary>
    /// Ensures a ManifestGroup exists with the given name, creating one if necessary.
    /// </summary>
    /// <returns>The ManifestGroup ID.</returns>
    public static async Task<long> EnsureManifestGroupAsync(
        this IDataContext context,
        string groupName,
        int priority,
        int? maxActiveJobs = null,
        bool isEnabled = true,
        CancellationToken ct = default
    )
    {
        var existing = await context.ManifestGroups.FirstOrDefaultAsync(
            g => g.Name == groupName,
            ct
        );

        if (existing != null)
        {
            existing.Priority = priority;
            existing.MaxActiveJobs = maxActiveJobs;
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            return existing.Id;
        }

        var group = new ManifestGroup
        {
            Name = groupName,
            Priority = priority,
            MaxActiveJobs = maxActiveJobs,
            IsEnabled = isEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.ManifestGroups.Add(group);
        await context.SaveChanges(ct);

        return group.Id;
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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
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
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(
            groupId,
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
            ct
        );

        var existing = await context.Manifests.FirstOrDefaultAsync(
            m => m.ExternalId == externalId,
            ct
        );

        if (existing != null)
        {
            // Update only scheduling-related fields, preserve runtime state
            existing.Name = trainType.FullName!;
            existing.SetProperties(input);
            existing.IsEnabled = options.IsEnabled;
            existing.MaxRetries = options.MaxRetries;
            existing.TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null;
            existing.ManifestGroupId = manifestGroupId;
            existing.Priority = options.Priority;
            ApplySchedule(existing, schedule);
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);

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
        ApplyMisfireOptions(manifest, options);
        ApplyExclusions(manifest, options);

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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
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
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(
            groupId,
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
            ct
        );

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
            existing.IsEnabled = options.IsEnabled;
            existing.MaxRetries = options.MaxRetries;
            existing.TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null;
            existing.ManifestGroupId = manifestGroupId;
            existing.Priority = options.Priority;
            existing.ScheduleType = scheduleType;
            existing.DependsOnManifestId = dependsOnManifestId;
            existing.CronExpression = null;
            existing.IntervalSeconds = null;
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);

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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
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
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
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
        int groupPriority,
        int? groupMaxActiveJobs = null,
        bool groupIsEnabled = true,
        CancellationToken ct = default
    )
    {
        var manifestGroupId = await context.EnsureManifestGroupAsync(
            groupId,
            groupPriority,
            groupMaxActiveJobs,
            groupIsEnabled,
            ct
        );

        var existing = await context.Manifests.FirstOrDefaultAsync(
            m => m.ExternalId == externalId,
            ct
        );

        if (existing != null)
        {
            existing.Name = trainType.FullName!;
            existing.SetProperties(input);
            existing.IsEnabled = options.IsEnabled;
            existing.MaxRetries = options.MaxRetries;
            existing.TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null;
            existing.ManifestGroupId = manifestGroupId;
            existing.Priority = options.Priority;
            existing.ScheduleType = ScheduleType.Once;
            existing.ScheduledAt = scheduledAt;
            existing.CronExpression = null;
            existing.IntervalSeconds = null;
            ApplyMisfireOptions(existing, options);
            ApplyExclusions(existing, options);

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

        context.Manifests.Add(manifest);

        return manifest;
    }

    /// <summary>
    /// Applies schedule configuration to a manifest.
    /// </summary>
    private static void ApplySchedule(Manifest manifest, Schedule schedule)
    {
        manifest.ScheduleType = schedule.Type;
        manifest.CronExpression = schedule.CronExpression;
        manifest.IntervalSeconds = schedule.Interval.HasValue
            ? (int)schedule.Interval.Value.TotalSeconds
            : null;
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
    /// Applies exclusion window configuration to a manifest from the options.
    /// </summary>
    private static void ApplyExclusions(Manifest manifest, ManifestOptions options)
    {
        manifest.SetExclusions(options.Exclusions);
    }
}
