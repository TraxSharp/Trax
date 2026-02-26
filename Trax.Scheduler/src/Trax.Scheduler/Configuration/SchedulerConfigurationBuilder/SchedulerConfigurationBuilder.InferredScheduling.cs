using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Services.ManifestScheduler;
using Trax.Effect.Services.ServiceTrain;
using LanguageExt;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    // ── Single-manifest methods (TWorkflow only) ─────────────────────────

    /// <summary>
    /// Schedules a workflow to run on a recurring basis.
    /// The input type is inferred from <typeparamref name="TWorkflow"/>'s
    /// <c>IServiceTrain&lt;TInput, Unit&gt;</c> interface.
    /// </summary>
    public SchedulerConfigurationBuilder Schedule<TWorkflow>(
        string externalId,
        IManifestProperties input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveAndValidate<TWorkflow>(input);

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    ((ManifestScheduler)scheduler).ScheduleAsyncUntyped(
                        workflowType,
                        inputType,
                        externalId,
                        input,
                        schedule,
                        options,
                        ct
                    ),
            }
        );

        _rootScheduledExternalId = externalId;
        _lastScheduledExternalId = externalId;

        return this;
    }

    /// <summary>
    /// Schedules a dependent workflow that runs after the root <c>Schedule</c> manifest succeeds.
    /// Always branches from the root, enabling fan-out patterns.
    /// </summary>
    public SchedulerConfigurationBuilder Include<TWorkflow>(
        string externalId,
        IManifestProperties input,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveAndValidate<TWorkflow>(input);

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
                    ((ManifestScheduler)scheduler).ScheduleDependentAsyncUntyped(
                        workflowType,
                        inputType,
                        externalId,
                        input,
                        parentExternalId,
                        options,
                        ct
                    ),
            }
        );

        _lastScheduledExternalId = externalId;

        return this;
    }

    /// <summary>
    /// Schedules a dependent workflow that runs after the previously scheduled manifest succeeds.
    /// Must be called after <c>Schedule</c>, <c>Include</c>, or another <c>ThenInclude</c>.
    /// </summary>
    public SchedulerConfigurationBuilder ThenInclude<TWorkflow>(
        string externalId,
        IManifestProperties input,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveAndValidate<TWorkflow>(input);

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
                    ((ManifestScheduler)scheduler).ScheduleDependentAsyncUntyped(
                        workflowType,
                        inputType,
                        externalId,
                        input,
                        parentExternalId,
                        options,
                        ct
                    ),
            }
        );

        _lastScheduledExternalId = externalId;

        return this;
    }

    // ── Batch methods (TWorkflow only, using ManifestItem) ───────────────

    /// <summary>
    /// Schedules multiple instances of a workflow from a collection of <see cref="ManifestItem"/>.
    /// Each item's <see cref="ManifestItem.Id"/> is used as the full external ID.
    /// </summary>
    public SchedulerConfigurationBuilder ScheduleMany<TWorkflow>(
        IEnumerable<ManifestItem> items,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveTypes<TWorkflow>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TWorkflow));

        var firstId = itemList.FirstOrDefault()?.Id ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var item in itemList)
            _externalIdToGroupId[item.Id] = resolved._groupId ?? item.Id;

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (batch of {itemList.Count})",
                ExpectedExternalIds = itemList.Select(i => i.Id).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await ((ManifestScheduler)scheduler).ScheduleManyAsyncUntyped(
                        workflowType,
                        inputType,
                        itemList,
                        item => (item.Id, item.Input),
                        schedule,
                        options,
                        configureEach: null,
                        ct
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
    /// Name-based overload of <c>ScheduleMany</c>.
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>,
    /// and external IDs as <c>"{name}-{item.Id}"</c>.
    /// </summary>
    public SchedulerConfigurationBuilder ScheduleMany<TWorkflow>(
        string name,
        IEnumerable<ManifestItem> items,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class =>
        ScheduleMany<TWorkflow>(
            items.Select(item => item with { Id = $"{name}-{item.Id}" }),
            schedule,
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            }
        );

    /// <summary>
    /// Schedules multiple dependent workflow instances.
    /// Items with <see cref="ManifestItem.DependsOn"/> set use that as the parent;
    /// items without fall back to the root <c>Schedule</c> manifest.
    /// </summary>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow>(
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveTypes<TWorkflow>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TWorkflow));

        // Only require _rootScheduledExternalId when at least one item lacks DependsOn
        var needsRoot = itemList.Any(item => item.DependsOn is null);
        string? rootExternalId = null;

        if (needsRoot)
        {
            rootExternalId =
                _rootScheduledExternalId
                ?? throw new InvalidOperationException(
                    "IncludeMany() must be called after Schedule(). "
                        + "No root manifest external ID is available."
                );
        }

        var firstId = itemList.FirstOrDefault()?.Id ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var item in itemList)
        {
            var parentExtId = item.DependsOn ?? rootExternalId!;
            _externalIdToGroupId[item.Id] = resolved._groupId ?? item.Id;
            _dependencyEdges.Add((parentExtId, item.Id));
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (dependent batch of {itemList.Count})",
                ExpectedExternalIds = itemList.Select(i => i.Id).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await (
                        (ManifestScheduler)scheduler
                    ).ScheduleManyDependentAsyncUntyped(
                        workflowType,
                        inputType,
                        itemList,
                        item => (item.Id, item.Input),
                        item => item.DependsOn ?? rootExternalId!,
                        options,
                        configureEach: null,
                        ct
                    );
                    return results.FirstOrDefault()!;
                },
            }
        );

        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Name-based overload of <c>IncludeMany</c>.
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>,
    /// and external IDs as <c>"{name}-{item.Id}"</c>.
    /// </summary>
    public SchedulerConfigurationBuilder IncludeMany<TWorkflow>(
        string name,
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class =>
        IncludeMany<TWorkflow>(
            items.Select(item => item with { Id = $"{name}-{item.Id}" }),
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            }
        );

    /// <summary>
    /// Schedules multiple dependent workflow instances for deeper chaining.
    /// Each item's <see cref="ManifestItem.DependsOn"/> must be set.
    /// </summary>
    public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow>(
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveTypes<TWorkflow>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TWorkflow));

        var firstId = itemList.FirstOrDefault()?.Id ?? "batch";

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);

        foreach (var item in itemList)
        {
            if (item.DependsOn is null)
                throw new InvalidOperationException(
                    $"ThenIncludeMany() requires DependsOn to be set on every ManifestItem. "
                        + $"Item '{item.Id}' has no DependsOn value."
                );

            _externalIdToGroupId[item.Id] = resolved._groupId ?? item.Id;
            _dependencyEdges.Add((item.DependsOn, item.Id));
        }

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = $"{firstId}... (dependent batch of {itemList.Count})",
                ExpectedExternalIds = itemList.Select(i => i.Id).ToList(),
                ScheduleFunc = async (scheduler, ct) =>
                {
                    var results = await (
                        (ManifestScheduler)scheduler
                    ).ScheduleManyDependentAsyncUntyped(
                        workflowType,
                        inputType,
                        itemList,
                        item => (item.Id, item.Input),
                        item => item.DependsOn!,
                        options,
                        configureEach: null,
                        ct
                    );
                    return results.FirstOrDefault()!;
                },
            }
        );

        _lastScheduledExternalId = null;

        return this;
    }

    /// <summary>
    /// Name-based overload of <c>ThenIncludeMany</c>.
    /// The <paramref name="name"/> automatically derives <c>groupId</c>, <c>prunePrefix</c>,
    /// and external IDs as <c>"{name}-{item.Id}"</c>.
    /// </summary>
    public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow>(
        string name,
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TWorkflow : class =>
        ThenIncludeMany<TWorkflow>(
            items.Select(item => item with { Id = $"{name}-{item.Id}" }),
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            }
        );

    // ── Type resolution helpers ──────────────────────────────────────────

    private static (Type WorkflowType, Type InputType) ResolveAndValidate<TWorkflow>(
        IManifestProperties input
    )
        where TWorkflow : class
    {
        var (workflowType, inputType) = ResolveTypes<TWorkflow>();

        if (input.GetType() != inputType)
            throw new InvalidOperationException(
                $"Input type mismatch: {workflowType.Name} expects input of type "
                    + $"'{inputType.Name}' (from IServiceTrain<{inputType.Name}, Unit>), "
                    + $"but received '{input.GetType().Name}'."
            );

        return (workflowType, inputType);
    }

    private static (Type WorkflowType, Type InputType) ResolveTypes<TWorkflow>()
        where TWorkflow : class
    {
        var workflowType = typeof(TWorkflow);
        var inputType = ResolveInputType(workflowType);
        return (workflowType, inputType);
    }

    private static void ValidateBatchInputType(
        List<ManifestItem> items,
        Type expectedInputType,
        Type workflowType
    )
    {
        foreach (var item in items)
        {
            if (item.Input.GetType() != expectedInputType)
                throw new InvalidOperationException(
                    $"Input type mismatch: {workflowType.Name} expects input of type "
                        + $"'{expectedInputType.Name}' (from IServiceTrain<{expectedInputType.Name}, Unit>), "
                        + $"but item '{item.Id}' has input of type '{item.Input.GetType().Name}'."
                );
        }
    }

    private static Type ResolveInputType(Type workflowType)
    {
        var effectInterface = workflowType
            .GetInterfaces()
            .FirstOrDefault(
                i =>
                    i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(IServiceTrain<,>)
                    && i.GetGenericArguments()[1] == typeof(Unit)
            );

        if (effectInterface is null)
            throw new InvalidOperationException(
                $"Type '{workflowType.Name}' must implement IServiceTrain<TInput, Unit> "
                    + $"to be used with Schedule<{workflowType.Name}>(). Found interfaces: "
                    + $"[{string.Join(", ", workflowType.GetInterfaces().Select(i => i.Name))}]"
            );

        var inputType = effectInterface.GetGenericArguments()[0];

        if (!typeof(IManifestProperties).IsAssignableFrom(inputType))
            throw new InvalidOperationException(
                $"Input type '{inputType.Name}' for workflow '{workflowType.Name}' "
                    + "must implement IManifestProperties."
            );

        return inputType;
    }
}
