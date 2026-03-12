using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Schedules a train to run on a recurring basis.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type</typeparam>
    /// <typeparam name="TInput">The input type for the train (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this scheduled job</param>
    /// <param name="input">The input data that will be passed to the train on each execution</param>
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
    /// builder.Services.AddTrax(trax => trax
    ///     .AddScheduler(scheduler => scheduler
    ///         .Schedule&lt;IHelloWorldTrain, HelloWorldInput&gt;(
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
    public SchedulerConfigurationBuilder Schedule<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : IServiceTrain<TInput, TOutput>
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
                    scheduler.ScheduleAsync<TTrain, TInput, TOutput>(
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
    /// Schedules a train to fire once after the specified delay, then auto-disable.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type</typeparam>
    /// <typeparam name="TInput">The input type for the train (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this one-off job</param>
    /// <param name="input">The input data that will be passed to the train on execution</param>
    /// <param name="delay">The delay before the job should execute</param>
    /// <param name="options">Optional callback to configure manifest options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder ScheduleOnce<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : IServiceTrain<TInput, TOutput>
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
                    scheduler.ScheduleOnceAsync<TTrain, TInput, TOutput>(
                        externalId,
                        input,
                        delay,
                        options,
                        ct: ct
                    ),
            }
        );

        _rootScheduledExternalId = null;
        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Schedules a dependent train that runs after the previously scheduled manifest succeeds.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type</typeparam>
    /// <typeparam name="TInput">The input type for the train (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this dependent job</param>
    /// <param name="input">The input data that will be passed to the train on each execution</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Must be called after <see cref="Schedule{TTrain,TInput}"/>, <see cref="Include{TTrain,TInput}"/>,
    /// or another <c>ThenInclude</c> call.
    /// The dependent manifest will be queued when the parent's LastSuccessfulRun is newer than its own.
    /// Supports chaining: <c>.Schedule(...).Include(...).ThenInclude(...)</c> for branched dependency chains.
    /// </remarks>
    public SchedulerConfigurationBuilder ThenInclude<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : IServiceTrain<TInput, TOutput>
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
                    scheduler.ScheduleDependentAsync<TTrain, TInput, TOutput>(
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
    /// Schedules a dependent train that runs after the root <see cref="Schedule{TTrain,TInput}"/> manifest succeeds.
    /// Unlike <see cref="ThenInclude{TTrain,TInput}"/> which chains from the most recent manifest,
    /// <c>Include</c> always branches from the root <c>Schedule</c>, enabling fan-out patterns.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type</typeparam>
    /// <typeparam name="TInput">The input type for the train (must implement IManifestProperties)</typeparam>
    /// <param name="externalId">A unique identifier for this dependent job</param>
    /// <param name="input">The input data that will be passed to the train on each execution</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Must be called after <see cref="Schedule{TTrain,TInput}"/>.
    /// Use <c>Include</c> to create multiple independent branches from a single root:
    /// <code>
    /// .Schedule&lt;A&gt;(...)           // root=A
    ///     .Include&lt;B&gt;(...)        // B depends on A (root)
    ///     .Include&lt;C&gt;(...)        // C depends on A (root)
    ///         .ThenInclude&lt;D&gt;(...)       // D depends on C (cursor)
    /// </code>
    /// Result: A → B, A → C → D
    /// </remarks>
    public SchedulerConfigurationBuilder Include<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : IServiceTrain<TInput, TOutput>
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
                    scheduler.ScheduleDependentAsync<TTrain, TInput, TOutput>(
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
