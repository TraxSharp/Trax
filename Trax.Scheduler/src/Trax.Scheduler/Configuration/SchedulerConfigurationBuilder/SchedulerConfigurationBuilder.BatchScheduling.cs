using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using LanguageExt;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Schedules multiple instances of a workflow from a collection.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="sources">The collection of source items to create manifests from</param>
    /// <param name="map">A function that transforms each source item into an ExternalId and Input pair</param>
    /// <param name="schedule">The schedule definition applied to all manifests</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The manifests are not created immediately. They are captured and will be seeded
    /// automatically on startup by the ManifestPollingService.
    /// All manifests are created in a single transaction.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrax.CoreEffects(options => options
    ///     .AddScheduler(scheduler => scheduler
    ///         .UseHangfire(/* ... */)
    ///         .ScheduleMany&lt;ISyncTableWorkflow, SyncTableInput, string&gt;(
    ///             new[] { "users", "orders", "products" },
    ///             table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    ///             Every.Minutes(5),
    ///             options => options
    ///                 .Group("sync", group => group.MaxActiveJobs(3))
    ///                 .PrunePrefix("sync-"))
    ///     )
    /// );
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder ScheduleMany<TWorkflow, TInput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        // Materialize the sources to avoid multiple enumeration
        var sourceList = sources.ToList();
        var firstId = sourceList.Select(s => map(s).ExternalId).FirstOrDefault() ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var source in sourceList)
        {
            var (extId, _) = map(source);
            _externalIdToGroupId[extId] = resolved._groupId ?? extId;
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (batch of {sourceList.Count})",
                ExpectedExternalIds = sourceList.Select(s => map(s).ExternalId).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await scheduler.ScheduleManyAsync<TWorkflow, TInput, TSource>(
                        sourceList,
                        map,
                        schedule,
                        options,
                        configureEach,
                        ct: ct
                    );
                    return results.FirstOrDefault()!;
                },
            }
        );

        _rootScheduledExternalId = null;
        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Schedules multiple instances of a workflow from a collection using a name-based convention.
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>, and
    /// the external ID prefix — reducing boilerplate when the naming follows the <c>{name}-{suffix}</c> pattern.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="name">The batch name. Used as <c>groupId</c>, <c>prunePrefix</c> is <c>"{name}-"</c>, and each external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="sources">The collection of items to create manifests from</param>
    /// <param name="map">A function that transforms each source item into a <c>Suffix</c> and <c>Input</c> pair. The full external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="schedule">The schedule definition applied to all manifests</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional callback to set per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler.ScheduleMany&lt;ISyncTableWorkflow, SyncTableInput, string&gt;(
    ///     "sync-table",
    ///     new[] { "users", "orders" },
    ///     table => (table, new SyncTableInput { TableName = table }),
    ///     Every.Minutes(5),
    ///     options => options
    ///         .Priority(10)
    ///         .Group(group => group.MaxActiveJobs(5)));
    /// // Creates manifests: sync-table-users, sync-table-orders
    /// // groupId: "sync-table", prunePrefix: "sync-table-"
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder ScheduleMany<TWorkflow, TInput, TSource>(
        string name,
        IEnumerable<TSource> sources,
        Func<TSource, (string Suffix, TInput Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        ScheduleMany<TWorkflow, TInput, TSource>(
            sources,
            source =>
            {
                var (suffix, input) = map(source);
                return ($"{name}-{suffix}", input);
            },
            schedule,
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            },
            configureEach
        );

    /// <summary>
    /// Schedules multiple dependent workflow instances for deeper chaining after a previous
    /// <c>IncludeMany</c>.
    /// Each dependent manifest is linked to its parent via the <paramref name="dependsOn"/> function.
    /// For first-level batch dependents after <see cref="ScheduleMany{TWorkflow,TInput,TSource}"/>,
    /// use the <c>IncludeMany</c> overload with <c>dependsOn</c> instead.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into an ExternalId and Input pair</param>
    /// <param name="dependsOn">A function that maps each source item to the external ID of its parent manifest</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler
    ///     .ScheduleMany&lt;IExtractWorkflow, ExtractInput, int&gt;(...)
    ///     .IncludeMany&lt;ITransformWorkflow, TransformInput, int&gt;(
    ///         ..., dependsOn: i => $"extract-{i}")
    ///     .ThenIncludeMany&lt;ILoadWorkflow, LoadInput, int&gt;(
    ///         Enumerable.Range(0, 10),
    ///         i => ($"load-{i}", new LoadInput { Index = i }),
    ///         dependsOn: i => $"transform-{i}",
    ///         options => options.Group("load", group => group.MaxActiveJobs(3)))
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow, TInput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var sourceList = sources.ToList();
        var firstId = sourceList.Select(s => map(s).ExternalId).FirstOrDefault() ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var source in sourceList)
        {
            var (extId, _) = map(source);
            var parentExtId = dependsOn(source);
            _externalIdToGroupId[extId] = resolved._groupId ?? extId;
            _dependencyEdges.Add((parentExtId, extId));
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (dependent batch of {sourceList.Count})",
                ExpectedExternalIds = sourceList.Select(s => map(s).ExternalId).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await scheduler.ScheduleManyDependentAsync<
                        TWorkflow,
                        TInput,
                        TSource
                    >(sourceList, map, dependsOn, options, configureEach, ct: ct);
                    return results.FirstOrDefault()!;
                },
            }
        );

        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Name-based overload of <c>ThenIncludeMany</c>.
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>, and
    /// the external ID prefix — reducing boilerplate when the naming follows the <c>{name}-{suffix}</c> pattern.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="name">The batch name. Used as <c>groupId</c>, <c>prunePrefix</c> is <c>"{name}-"</c>, and each external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into a <c>Suffix</c> and <c>Input</c> pair. The full external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="dependsOn">A function that maps each source item to the external ID of its parent manifest</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler
    ///     .ScheduleMany&lt;IExtractWorkflow, ExtractInput, int&gt;("extract", ...)
    ///     .IncludeMany&lt;ITransformWorkflow, TransformInput, int&gt;("transform",
    ///         ..., dependsOn: i => $"extract-{i}")
    ///     .ThenIncludeMany&lt;ILoadWorkflow, LoadInput, int&gt;("load",
    ///         Enumerable.Range(0, 10),
    ///         i => ($"{i}", new LoadInput { Index = i }),
    ///         dependsOn: i => $"transform-{i}")
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow, TInput, TSource>(
        string name,
        IEnumerable<TSource> sources,
        Func<TSource, (string Suffix, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        ThenIncludeMany<TWorkflow, TInput, TSource>(
            sources,
            source =>
            {
                var (suffix, input) = map(source);
                return ($"{name}-{suffix}", input);
            },
            dependsOn,
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            },
            configureEach
        );

    /// <summary>
    /// Schedules multiple dependent workflow instances that each depend on the root <see cref="Schedule{TWorkflow,TInput}"/> manifest.
    /// Unlike <see cref="ThenIncludeMany{TWorkflow,TInput,TSource}"/> which requires an explicit <c>dependsOn</c> function,
    /// <c>IncludeMany</c> automatically parents all items from the root <c>Schedule</c>, enabling fan-out patterns.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into an ExternalId and Input pair</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Must be called after <see cref="Schedule{TWorkflow,TInput}"/>.
    /// <code>
    /// .Schedule&lt;A&gt;("root", inputA, Every.Minutes(5))
    /// .IncludeMany&lt;B, InputB, int&gt;(
    ///     Enumerable.Range(0, 10),
    ///     i =&gt; ($"child-{i}", new InputB { Index = i }),
    ///     options => options.Group("children", group => group.MaxActiveJobs(5)))
    /// // All 10 child manifests depend on "root" (A)
    /// </code>
    /// </remarks>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow, TInput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var rootExternalId =
            _rootScheduledExternalId
            ?? throw new InvalidOperationException(
                "IncludeMany() must be called after Schedule(). "
                    + "No root manifest external ID is available."
            );

        var sourceList = sources.ToList();
        var firstId = sourceList.Select(s => map(s).ExternalId).FirstOrDefault() ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var source in sourceList)
        {
            var (extId, _) = map(source);
            _externalIdToGroupId[extId] = resolved._groupId ?? extId;
            _dependencyEdges.Add((rootExternalId, extId));
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (dependent batch of {sourceList.Count})",
                ExpectedExternalIds = sourceList.Select(s => map(s).ExternalId).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await scheduler.ScheduleManyDependentAsync<
                        TWorkflow,
                        TInput,
                        TSource
                    >(sourceList, map, _ => rootExternalId, options, configureEach, ct: ct);
                    return results.FirstOrDefault()!;
                },
            }
        );

        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Name-based overload of <c>IncludeMany</c> (root-based).
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>, and
    /// the external ID prefix — reducing boilerplate when the naming follows the <c>{name}-{suffix}</c> pattern.
    /// All items automatically depend on the root <see cref="Schedule{TWorkflow,TInput}"/> manifest.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="name">The batch name. Used as <c>groupId</c>, <c>prunePrefix</c> is <c>"{name}-"</c>, and each external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into a <c>Suffix</c> and <c>Input</c> pair. The full external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler
    ///     .Schedule&lt;IExtractWorkflow, ExtractInput&gt;(
    ///         "extract-all", new ExtractInput(), Every.Hours(1))
    ///     .IncludeMany&lt;ILoadWorkflow, LoadInput, int&gt;("load",
    ///         Enumerable.Range(0, 10),
    ///         i => ($"{i}", new LoadInput { Partition = i }),
    ///         options => options.Group(group => group.MaxActiveJobs(5)))
    /// // Creates: load-0, load-1, ... load-9 (all depend on extract-all)
    /// // groupId: "load", prunePrefix: "load-"
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow, TInput, TSource>(
        string name,
        IEnumerable<TSource> sources,
        Func<TSource, (string Suffix, TInput Input)> map,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        IncludeMany<TWorkflow, TInput, TSource>(
            sources,
            source =>
            {
                var (suffix, input) = map(source);
                return ($"{name}-{suffix}", input);
            },
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            },
            configureEach
        );

    /// <summary>
    /// Schedules multiple dependent workflow instances from a collection, where each item
    /// explicitly maps to its parent via the <paramref name="dependsOn"/> function.
    /// Use after <see cref="ScheduleMany{TWorkflow,TInput,TSource}"/> for first-level batch dependents,
    /// or after <see cref="Schedule{TWorkflow,TInput}"/> when explicit per-item parent mapping is needed.
    /// For deeper chaining after a previous <c>IncludeMany</c>, use <c>ThenIncludeMany</c>.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into an ExternalId and Input pair</param>
    /// <param name="dependsOn">A function that maps each source item to the external ID of its parent manifest</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler
    ///     .ScheduleMany&lt;IExtractWorkflow, ExtractInput, int&gt;(
    ///         Enumerable.Range(0, 10),
    ///         i => ($"extract-{i}", new ExtractInput { Index = i }),
    ///         Every.Minutes(5),
    ///         options => options.Group("extract"))
    ///     .IncludeMany&lt;ITransformWorkflow, TransformInput, int&gt;(
    ///         Enumerable.Range(0, 10),
    ///         i => ($"transform-{i}", new TransformInput { Index = i }),
    ///         dependsOn: i => $"extract-{i}",
    ///         options => options.Group("transform", group => group.MaxActiveJobs(5)))
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow, TInput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        var sourceList = sources.ToList();
        var firstId = sourceList.Select(s => map(s).ExternalId).FirstOrDefault() ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var source in sourceList)
        {
            var (extId, _) = map(source);
            var parentExtId = dependsOn(source);
            _externalIdToGroupId[extId] = resolved._groupId ?? extId;
            _dependencyEdges.Add((parentExtId, extId));
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (dependent batch of {sourceList.Count})",
                ExpectedExternalIds = sourceList.Select(s => map(s).ExternalId).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await scheduler.ScheduleManyDependentAsync<
                        TWorkflow,
                        TInput,
                        TSource
                    >(sourceList, map, dependsOn, options, configureEach, ct: ct);
                    return results.FirstOrDefault()!;
                },
            }
        );

        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Name-based overload of <c>IncludeMany</c> (with dependsOn).
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>, and
    /// the external ID prefix — reducing boilerplate when the naming follows the <c>{name}-{suffix}</c> pattern.
    /// Each item explicitly maps to its parent via the <paramref name="dependsOn"/> function.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow interface type</typeparam>
    /// <typeparam name="TInput">The input type for the workflow (must implement IManifestProperties)</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection</typeparam>
    /// <param name="name">The batch name. Used as <c>groupId</c>, <c>prunePrefix</c> is <c>"{name}-"</c>, and each external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="sources">The collection of source items to create dependent manifests from</param>
    /// <param name="map">A function that transforms each source item into a <c>Suffix</c> and <c>Input</c> pair. The full external ID is <c>"{name}-{suffix}"</c>.</param>
    /// <param name="dependsOn">A function that maps each source item to the external ID of its parent manifest</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/></param>
    /// <param name="configureEach">Optional action to configure per-item manifest options</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// scheduler
    ///     .ScheduleMany&lt;IExtractWorkflow, ExtractInput, int&gt;("extract", ...)
    ///     .IncludeMany&lt;ITransformWorkflow, TransformInput, int&gt;("transform",
    ///         Enumerable.Range(0, 10),
    ///         i => ($"{i}", new TransformInput { Index = i }),
    ///         dependsOn: i => $"extract-{i}")
    /// </code>
    /// </example>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow, TInput, TSource>(
        string name,
        IEnumerable<TSource> sources,
        Func<TSource, (string Suffix, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties =>
        IncludeMany<TWorkflow, TInput, TSource>(
            sources,
            source =>
            {
                var (suffix, input) = map(source);
                return ($"{name}-{suffix}", input);
            },
            dependsOn,
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            },
            configureEach
        );
}
