using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Schedules a workflow to run on a recurring basis.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this scheduled job</param>
    /// <param name="input">The input data that will be passed to the workflow on each execution</param>
    /// <param name="schedule">The schedule definition (interval or cron-based)</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The manifest is not created immediately. It is captured and will be seeded
    /// automatically on startup by the ManifestPollingService.
    /// All scheduled manifests use upsert semantics based on ExternalId.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTraxEffects(options => options
    ///     .AddScheduler(scheduler => scheduler
    ///         .UseHangfire(/* ... */)
    ///         .Schedule&lt;IHelloWorldWorkflow, HelloWorldInput&gt;(
    ///             "hello-world",
    ///             new HelloWorldInput { Name = "Scheduler" },
    ///             Every.Minutes(1),
    ///             options => options
    ///                 .Priority(10)
    ///                 .Group(group => group.MaxActiveJobs(5)))
    ///     )
    /// );
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder Schedule<TWorkflow, TInput>(
        string externalId,
        TInput input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    scheduler.ScheduleAsync<TWorkflow, TInput>(
                        externalId,
                        input,
                        schedule,
                        options,
                        ct: ct
                    ),
            }
        );

        _rootScheduledExternalId = externalId;
        _lastScheduledExternalId = externalId;

        return this;
    }

    /// <summary>
    /// Schedules a dependent workflow that runs after the previously scheduled manifest succeeds.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this dependent job</param>
    /// <param name="input">The input data that will be passed to the workflow on each execution</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Must be called after <see cref="Schedule{TWorkflow,TInput}"/>, <see cref="Include{TWorkflow,TInput}"/>,
    /// or another <c>ThenInclude</c> call.
    /// The dependent manifest will be queued when the parent's LastSuccessfulRun is newer than its own.
    /// Supports chaining: <c>.Schedule(...).Include(...).ThenInclude(...)</c> for branched dependency chains.
    /// </remarks>
    public SchedulerConfigurationBuilder ThenInclude<TWorkflow, TInput>(
        string externalId,
        TInput input,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var parentExternalId =
            _lastScheduledExternalId
            ?? throw new InvalidOperationException(
                "ThenInclude() must be called after Schedule(), Include(), or another ThenInclude(). "
                    + "No parent manifest external ID is available."
            );

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;
        _dependencyEdges.Add((parentExternalId, externalId));

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    scheduler.ScheduleDependentAsync<TWorkflow, TInput>(
                        externalId,
                        input,
                        parentExternalId,
                        options,
                        ct: ct
                    ),
            }
        );

        _lastScheduledExternalId = externalId;

        return this;
    }

    /// <summary>
    /// Schedules a dependent workflow that runs after the root <see cref="Schedule{TWorkflow,TInput}"/> manifest succeeds.
    /// Unlike <see cref="ThenInclude{TWorkflow,TInput}"/> which chains from the most recent manifest,
    /// <c>Include</c> always branches from the root <c>Schedule</c>, enabling fan-out patterns.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this dependent job</param>
    /// <param name="input">The input data that will be passed to the workflow on each execution</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Must be called after <see cref="Schedule{TWorkflow,TInput}"/>.
    /// Use <c>Include</c> to create multiple independent branches from a single root:
    /// <code>
    /// .Schedule&lt;A&gt;(...)           // root=A
    ///     .Include&lt;B&gt;(...)        // B depends on A (root)
    ///     .Include&lt;C&gt;(...)        // C depends on A (root)
    ///         .ThenInclude&lt;D&gt;(...)       // D depends on C (cursor)
    /// </code>
    /// Result: A → B, A → C → D
    /// </remarks>
    public SchedulerConfigurationBuilder Include<TWorkflow, TInput>(
        string externalId,
        TInput input,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var parentExternalId =
            _rootScheduledExternalId
            ?? throw new InvalidOperationException(
                "Include() must be called after Schedule(). "
                    + "No root manifest external ID is available."
            );

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;
        _dependencyEdges.Add((parentExternalId, externalId));

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    scheduler.ScheduleDependentAsync<TWorkflow, TInput>(
                        externalId,
                        input,
                        parentExternalId,
                        options,
                        ct: ct
                    ),
            }
        );

        _lastScheduledExternalId = externalId;

        return this;
    }
}
