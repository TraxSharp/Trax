using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Configuration;
using Trax.Effect.Services.ServiceTrain;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
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
    public static Task<Manifest> UpsertManifestAsync<TWorkflow, TInput>(
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
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        context.UpsertManifestAsync(
            typeof(TWorkflow),
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
    /// Non-generic overload that accepts workflow type as a <see cref="Type"/> parameter.
    /// </summary>
    internal static async Task<Manifest> UpsertManifestAsync(
        this IDataContext context,
        Type workflowType,
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
            existing.Name = workflowType.FullName!;
            existing.SetProperties(input);
            existing.IsEnabled = options.IsEnabled;
            existing.MaxRetries = options.MaxRetries;
            existing.TimeoutSeconds = options.Timeout.HasValue
                ? (int)options.Timeout.Value.TotalSeconds
                : null;
            existing.ManifestGroupId = manifestGroupId;
            existing.Priority = options.Priority;
            ApplySchedule(existing, schedule);

            return existing;
        }

        // Create new manifest
        var manifest = new Manifest
        {
            ExternalId = externalId,
            Name = workflowType.FullName!,
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

        context.Manifests.Add(manifest);

        return manifest;
    }

    /// <summary>
    /// Creates or updates a dependent manifest that triggers after a parent manifest succeeds.
    /// </summary>
    public static Task<Manifest> UpsertDependentManifestAsync<TWorkflow, TInput>(
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
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        context.UpsertDependentManifestAsync(
            typeof(TWorkflow),
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
    /// Non-generic overload that accepts workflow type as a <see cref="Type"/> parameter.
    /// </summary>
    internal static async Task<Manifest> UpsertDependentManifestAsync(
        this IDataContext context,
        Type workflowType,
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
            existing.Name = workflowType.FullName!;
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

            return existing;
        }

        var manifest = new Manifest
        {
            ExternalId = externalId,
            Name = workflowType.FullName!,
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
}
