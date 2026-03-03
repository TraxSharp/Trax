using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.TraxScheduler;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    // ── Single-manifest methods (TTrain only) ─────────────────────────

    /// <summary>
    /// Schedules a train to run on a recurring basis.
    /// The input type is inferred from <typeparamref name="TTrain"/>'s
    /// <c>IServiceTrain&lt;TInput, Unit&gt;</c> interface.
    /// </summary>
    public SchedulerConfigurationBuilder Schedule<TTrain>(
        string externalId,
        IManifestProperties input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveAndValidate<TTrain>(input);

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    ((TraxScheduler)scheduler).ScheduleAsyncUntyped(
                        trainType,
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
    /// Schedules a dependent train that runs after the root <c>Schedule</c> manifest succeeds.
    /// Always branches from the root, enabling fan-out patterns.
    /// </summary>
    public SchedulerConfigurationBuilder Include<TTrain>(
        string externalId,
        IManifestProperties input,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveAndValidate<TTrain>(input);

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
                    ((TraxScheduler)scheduler).ScheduleDependentAsyncUntyped(
                        trainType,
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
    /// Schedules a dependent train that runs after the previously scheduled manifest succeeds.
    /// Must be called after <c>Schedule</c>, <c>Include</c>, or another <c>ThenInclude</c>.
    /// </summary>
    public SchedulerConfigurationBuilder ThenInclude<TTrain>(
        string externalId,
        IManifestProperties input,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveAndValidate<TTrain>(input);

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
                    ((TraxScheduler)scheduler).ScheduleDependentAsyncUntyped(
                        trainType,
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

    // ── One-off methods (TTrain only) ──────────────────────────────────

    /// <summary>
    /// Schedules a train to fire once after the specified delay, then auto-disable.
    /// The input type is inferred from <typeparamref name="TTrain"/>'s
    /// <c>IServiceTrain&lt;TInput, Unit&gt;</c> interface.
    /// </summary>
    public SchedulerConfigurationBuilder ScheduleOnce<TTrain>(
        string externalId,
        IManifestProperties input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveAndValidate<TTrain>(input);

        var resolved = new ScheduleOptions();
        options?.Invoke(resolved);
        _externalIdToGroupId[externalId] = resolved._groupId ?? externalId;

        _configuration.PendingManifests.Add(
            new PendingManifest
            {
                ExternalId = externalId,
                ExpectedExternalIds = [externalId],
                ScheduleFunc = (scheduler, ct) =>
                    ((TraxScheduler)scheduler).ScheduleOnceAsyncUntyped(
                        trainType,
                        inputType,
                        externalId,
                        input,
                        delay,
                        options,
                        ct
                    ),
            }
        );

        _rootScheduledExternalId = null;
        _lastScheduledExternalId = null;

        return this;
    }

    // ── Batch methods (TTrain only, using ManifestItem) ───────────────

    /// <summary>
    /// Schedules multiple instances of a train from a collection of <see cref="ManifestItem"/>.
    /// Each item's <see cref="ManifestItem.Id"/> is used as the full external ID.
    /// </summary>
    public SchedulerConfigurationBuilder ScheduleMany<TTrain>(
        IEnumerable<ManifestItem> items,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveTypes<TTrain>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TTrain));

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
                    var results = await ((TraxScheduler)scheduler).ScheduleManyAsyncUntyped(
                        trainType,
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
    public SchedulerConfigurationBuilder ScheduleMany<TTrain>(
        string name,
        IEnumerable<ManifestItem> items,
        Schedule schedule,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class =>
        ScheduleMany<TTrain>(
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
    /// Schedules multiple dependent train instances.
    /// Items with <see cref="ManifestItem.DependsOn"/> set use that as the parent;
    /// items without fall back to the root <c>Schedule</c> manifest.
    /// </summary>
    public SchedulerConfigurationBuilder IncludeMany<TTrain>(
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveTypes<TTrain>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TTrain));

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
                        (TraxScheduler)scheduler
                    ).ScheduleManyDependentAsyncUntyped(
                        trainType,
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
    public SchedulerConfigurationBuilder IncludeMany<TTrain>(
        string name,
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class =>
        IncludeMany<TTrain>(
            items.Select(item => item with { Id = $"{name}-{item.Id}" }),
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            }
        );

    /// <summary>
    /// Schedules multiple dependent train instances for deeper chaining.
    /// Each item's <see cref="ManifestItem.DependsOn"/> must be set.
    /// </summary>
    public SchedulerConfigurationBuilder ThenIncludeMany<TTrain>(
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveTypes<TTrain>();
        var itemList = items.ToList();
        ValidateBatchInputType(itemList, inputType, typeof(TTrain));

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
                        (TraxScheduler)scheduler
                    ).ScheduleManyDependentAsyncUntyped(
                        trainType,
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
    public SchedulerConfigurationBuilder ThenIncludeMany<TTrain>(
        string name,
        IEnumerable<ManifestItem> items,
        Action<ScheduleOptions>? options = null
    )
        where TTrain : class =>
        ThenIncludeMany<TTrain>(
            items.Select(item => item with { Id = $"{name}-{item.Id}" }),
            opts =>
            {
                opts.Group(name);
                opts.PrunePrefix($"{name}-");
                options?.Invoke(opts);
            }
        );

    // ── Type resolution helpers ──────────────────────────────────────────

    private static (Type TrainType, Type InputType) ResolveAndValidate<TTrain>(
        IManifestProperties input
    )
        where TTrain : class
    {
        var (trainType, inputType) = ResolveTypes<TTrain>();

        if (input.GetType() != inputType)
            throw new InvalidOperationException(
                $"Input type mismatch: {trainType.Name} expects input of type "
                    + $"'{inputType.Name}' (from IServiceTrain<{inputType.Name}, Unit>), "
                    + $"but received '{input.GetType().Name}'."
            );

        return (trainType, inputType);
    }

    private static (Type TrainType, Type InputType) ResolveTypes<TTrain>()
        where TTrain : class
    {
        var trainType = typeof(TTrain);
        var inputType = ResolveInputType(trainType);
        return (trainType, inputType);
    }

    private static void ValidateBatchInputType(
        List<ManifestItem> items,
        Type expectedInputType,
        Type trainType
    )
    {
        foreach (var item in items)
        {
            if (item.Input.GetType() != expectedInputType)
                throw new InvalidOperationException(
                    $"Input type mismatch: {trainType.Name} expects input of type "
                        + $"'{expectedInputType.Name}' (from IServiceTrain<{expectedInputType.Name}, Unit>), "
                        + $"but item '{item.Id}' has input of type '{item.Input.GetType().Name}'."
                );
        }
    }

    private static Type ResolveInputType(Type trainType)
    {
        var effectInterface = trainType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IServiceTrain<,>)
                && i.GetGenericArguments()[1] == typeof(Unit)
            );

        if (effectInterface is null)
            throw new InvalidOperationException(
                $"Type '{trainType.Name}' must implement IServiceTrain<TInput, Unit> "
                    + $"to be used with Schedule<{trainType.Name}>(). Found interfaces: "
                    + $"[{string.Join(", ", trainType.GetInterfaces().Select(i => i.Name))}]"
            );

        var inputType = effectInterface.GetGenericArguments()[0];

        if (!typeof(IManifestProperties).IsAssignableFrom(inputType))
            throw new InvalidOperationException(
                $"Input type '{inputType.Name}' for train '{trainType.Name}' "
                    + "must implement IManifestProperties."
            );

        return inputType;
    }
}
