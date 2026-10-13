using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SnapshotNotices;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Registers state-machine persistence as a first-class Trax subsystem inside <c>AddTrax</c>. One call
/// discovers the machines, wires the store, the effect-claim ledger, the exactly-once runner, the registry,
/// and the six generic <c>stateMachine</c> mutations, and contributes the mutations to the mediator scan. The
/// stores reach their tables through the <see cref="IDataContext"/> the host's data provider registers, so the
/// host writes one call and nothing else: no <c>DbContext</c> of its own, no naming of the mutations' assembly.
/// </summary>
public static class StateMachinesBuilderExtensions
{
    /// <summary>
    /// Add state-machine persistence, discovering machines in <paramref name="assemblies"/>. A machine the host
    /// registers as <see cref="IMachine"/> itself is one of the subsystem's machines too. Each machine marked
    /// <see cref="TraxMachineAttribute"/>, however it was registered, is served to clients. Call after
    /// <c>AddEffects(... .UsePostgres/.UseSqlite/.UseInMemory ...)</c> and before <c>AddMediator(...)</c>.
    /// </summary>
    public static TraxBuilderWithEffects AddStateMachines(
        this TraxBuilderWithEffects builder,
        params Assembly[] assemblies
    ) => builder.AddStateMachines(null, assemblies);

    /// <summary>
    /// Add state-machine persistence with host-level options (see <see cref="StateMachineOptions"/>, e.g. the
    /// draft TTL). Call after <c>AddEffects(...)</c> and before <c>AddMediator(...)</c>:
    /// <code>
    /// services.AddTrax(trax => trax
    ///     .AddEffects(e => e.UsePostgres(conn).AddJson())
    ///     .AddStateMachines(typeof(MyMachine).Assembly)
    ///     .AddMediator(m => m.ScanAssemblies(typeof(MyTrain).Assembly)));
    /// </code>
    /// </summary>
    public static TraxBuilderWithEffects AddStateMachines(
        this TraxBuilderWithEffects builder,
        Action<StateMachineOptions>? configure,
        params Assembly[] assemblies
    )
    {
        if (!builder.HasDataProvider)
            throw new InvalidOperationException(
                "AddStateMachines() requires a data provider. Configure one in AddEffects() first: "
                    + ".UsePostgres(connectionString), .UseSqlite(connectionString), or .UseInMemory(). "
                    + "The snapshot store needs a database to persist drafts and effect claims."
            );

        if (builder.Root.MediatorConfigured)
            throw new InvalidOperationException(
                "AddStateMachines() must be called BEFORE AddMediator(). The generic stateMachine mutations are "
                    + "routed by the mediator, which builds its registry when AddMediator() runs; adding them "
                    + "afterwards leaves them undispatchable. Reorder the chain: "
                    + "AddEffects(...).AddStateMachines(...).AddMediator(...)."
            );

        // One call configures the subsystem. A second would replace the first's options (its opt-in and caps) and register every machine and service again, so it is refused instead.
        if (builder.ServiceCollection.Any(d => d.ServiceType == typeof(StateMachineOptions)))
            throw new InvalidOperationException(
                "AddStateMachines() was called more than once. Call it once, passing every assembly that holds "
                    + "your machines and configuring every option in that call: "
                    + "AddStateMachines(o => ..., typeof(A).Assembly, typeof(B).Assembly)."
            );

        RegisterMachinesAndStores(
            builder.ServiceCollection,
            configure,
            assemblies,
            inMemory: !builder.HasDatabaseProvider
        );

        // Contribute the generic mutations' assembly so AddMediator scans it and the six mutations become
        // dispatchable, without the host naming the assembly.
        builder.Root.ContributedMediatorAssemblies.Add(StateMachineMutations.Assembly);

        return builder;
    }

    // Discover the machines and wire the store, the effect-claim ledger, the exactly-once runner, the machine
    // registry, and the six generic stateMachine mutation routes.
    private static void RegisterMachinesAndStores(
        IServiceCollection services,
        Action<StateMachineOptions>? configure,
        Assembly[] assemblies,
        bool inMemory
    )
    {
        var options = new StateMachineOptions();
        configure?.Invoke(options);

        // The InMemory provider has no transactions, so it cannot count a user's drafts and insert one atomically: a
        // cap the host sets there is refused rather than held loosely, and the default is not applied.
        if (inMemory)
        {
            if (options is { MaxDraftsPerUserSet: true, MaxDraftsPerUser: not null })
                throw new InvalidOperationException(
                    "StateMachineOptions.MaxDraftsPerUser is set, but the data provider is InMemory, which has no "
                        + "transactions to count a user's drafts and insert one in: concurrent starts could pass the "
                        + "cap together. Use UsePostgres(...) or UseSqlite(...), or leave MaxDraftsPerUser unset."
                );
            options.CapUnavailable = true;
        }

        // A TTL the host set longer than the lifetime would never be reached: the lifetime deletes the draft first.
        // The host is told so rather than left believing its drafts live that long. A lifetime set below the default
        // TTL, with no TTL of the host's own, is a plain cap.
        if (
            options is { DraftTtlSet: true, DraftTtl: { } ttl, MaxDraftLifetime: { } lifetime }
            && ttl > lifetime
        )
            throw new InvalidOperationException(
                $"{nameof(StateMachineOptions.DraftTtl)} ({ttl}) is longer than "
                    + $"{nameof(StateMachineOptions.MaxDraftLifetime)} ({lifetime}), so a draft would be deleted "
                    + $"at {nameof(StateMachineOptions.MaxDraftLifetime)} before it could ever be idle that long. "
                    + $"Raise {nameof(StateMachineOptions.MaxDraftLifetime)} to at least "
                    + $"{nameof(StateMachineOptions.DraftTtl)}, or set it to null for no bound."
            );

        // The generic mutations fail closed: serving them names the policy that gates them.
        if (options.GenericMutationsExposed)
        {
            if (string.IsNullOrWhiteSpace(options.GenericMutationsPolicy))
                throw new InvalidOperationException(
                    "ExposeGenericStateMachineMutations(...) was called without a "
                        + $"{nameof(GenericStateMachineMutationOptions)}.{nameof(GenericStateMachineMutationOptions.Policy)}. "
                        + "The six generic stateMachine mutations reach every user-owned machine, so they are served "
                        + "only behind an authorization policy the host names: "
                        + "ExposeGenericStateMachineMutations(o => o.Policy = \"...\")."
                );
            foreach (var (train, description) in GenericMutations)
                services.AddSingleton(
                    new TrainMutationExposure(
                        train,
                        new TraxMutationAttribute(GraphQLOperation.Run)
                        {
                            Namespace = "stateMachine",
                            Description = description,
                        },
                        options.GenericMutationsPolicy
                    )
                );
        }

        services.AddSingleton(options);

        var machineTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsAbstract: false, IsClass: true, ContainsGenericParameters: false }
                && typeof(IMachine).IsAssignableFrom(type)
            )
            .Distinct()
            .ToList();

        // A machine marked [TraxMachine] is served, however it is registered: by this scan or by the host's own
        // IMachine registration (StateMachineOptions.IsServed reads the attribute off each registered machine). The
        // attribute on anything else the scan finds (an abstract or open generic machine, or a class that is no
        // machine) would serve nothing, so it refuses the host rather than leave the author believing it is served.
        var misplaced = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type.IsDefined(typeof(TraxMachineAttribute), inherit: false)
                && !machineTypes.Contains(type)
            )
            .Select(type => type.FullName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        if (misplaced.Count > 0)
            throw new InvalidOperationException(
                "[TraxMachine] is on a type in a scanned assembly that the scan cannot register, so it would serve "
                    + "nothing: "
                    + string.Join(", ", misplaced)
                    + ". The scan registers concrete, non-generic Machine<TState, TTrigger> classes: put the attribute "
                    + "on such a class (it is not inherited). A closed generic machine is served only when the host "
                    + "registers it as IMachine itself, by its closed type, from an assembly it does not scan."
            );

        // A machine the host registered as IMachine itself before this call is not registered again: one type is one
        // machine. Two types under one id (a library's machine and the marked subclass a host serves it through) are
        // refused at startup, once the host's own registrations are in.
        foreach (var type in machineTypes)
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IMachine), type));

        // The stores use the request's data context, and the provider's dialect to read a unique violation as a
        // lost race. InMemory registers no dialect, so there a race throws rather than being misread.
        // Each write records the IR hash of the definition that made it, so a machine whose hash cannot be
        // computed refuses the host's start rather than failing every write.
        services.AddSingleton<MachineIrHashes>();
        services.AddSingleton<MachineTimerLookup>();
        services.AddHostedService<MachineSchemaStartupValidator>();
        services.AddScoped<ISnapshotStore>(sp => new EfSnapshotStore(
            sp.GetRequiredService<IDataContext>(),
            sp.GetService<ISqlDialect>()
        )
        {
            IrHashes = sp.GetRequiredService<MachineIrHashes>().Of,
            Timers = sp.GetRequiredService<MachineTimerLookup>().Of,
        });
        services.AddScoped<IEffectClaimStore>(sp => new EfEffectClaimStore(
            sp.GetRequiredService<IDataContext>(),
            sp.GetService<ISqlDialect>()
        ));
        services.AddScoped<IdempotentEffect>();
        services.AddScoped<ISnapshotMachineRegistry, SnapshotMachineRegistry>();

        // System-owned instances and invoke tokens: server-only, so reached through the provider's data context
        // whatever ISnapshotStore a host substitutes. The outbox writes a snapshot entering or leaving an invoking
        // state together with its run, through the launcher Trax.Mediator registers; the cancellation cancels a
        // run by its token, as the operations surface does. The startup check refuses a host whose machines
        // invoke a train it cannot run, cancel or authorize as declared.
        services.AddScoped<IMachineInstanceStore>(sp => new EfSnapshotStore(
            sp.GetRequiredService<IDataContext>(),
            sp.GetService<ISqlDialect>()
        )
        {
            IrHashes = sp.GetRequiredService<MachineIrHashes>().Of,
            Timers = sp.GetRequiredService<MachineTimerLookup>().Of,
        });
        services.AddScoped<InvokeOutbox>();
        services.AddScoped<IMachineInstances, MachineInstances>();
        services.AddScoped<IInvokedRunCancellation, InvokedRunCancellation>();
        services.AddHostedService<InvokesStartupValidator>();
        // A machine a client can reach (exposed, or every user-owned one while the generic mutations are opted in)
        // declares a rule for everything a client can do to it, or the host does not start.
        services.AddHostedService<MachineRulesStartupValidator>();
        // Two machine types under one id would break every machine request, so the host does not start. It runs after
        // the rules check, whose refusal of an unmarked subclass of a marked machine names the better remedy.
        services.AddHostedService<MachineIdStartupValidator>();

        // An invoked run's outcome comes back to the state that queued it: the hook applies it on the host that ran
        // the train, and the reconciler, on every host that registers machines, sweeps for the ones the hook missed.
        // Both deliver through one conditional update on the token, so an outcome is applied once however many
        // hosts deliver it. The hook is not toggleable: the registry leaves an untracked factory enabled.
        services.AddScoped<InvokeOutcomeDelivery>();
        services.AddSingleton<InvokeOutcomeReconciler>();
        services.AddHostedService(sp => sp.GetRequiredService<InvokeOutcomeReconciler>());
        services.AddSingleton<ITrainLifecycleHookFactory, InvokeOutcomeHookFactory>();
        services.AddSingleton<IInvokedRunOutcomes, InvokedRunOutcomes>();

        // A start whose reduction reaches a field only the server writes refuses the host.
        services.AddHostedService<StartDeclarationValidator>();

        // Drafts idle past the TTL are deleted by a sweep on every host that registers machines, never by a read.
        services.AddSingleton<DraftSweeper>();
        services.AddHostedService(sp => sp.GetRequiredService<DraftSweeper>());

        // The change feed: one listener per host (a Postgres session on the provider's notices, or a poll of the
        // table on SQLite and InMemory, and on Postgres when the host chose to poll) and the hub that re-reads a
        // changed row for whoever watches it. Neither opens anything until something watches. Stopped in reverse
        // order, so the hub lets go of the listener first. The startup check compares the chosen feed with what the
        // database sends.
        services.AddSingleton(sp => new SnapshotChangeListener(
            sp.GetRequiredService<IDataContextProviderFactory>(),
            sp.GetService<ILogger<SnapshotChangeListener>>(),
            options.ChangeFeed == StateMachineChangeFeed.Poll
                ? null
                : sp.GetService<ISnapshotNoticeSource>()
        ));
        services.AddHostedService(sp => new ChangeFeedStartupValidator(
            options,
            sp.GetService<ISnapshotNoticeSource>(),
            sp.GetService<ILogger<ChangeFeedStartupValidator>>()
        ));
        services.AddSingleton<ISnapshotChangeListener>(sp =>
            sp.GetRequiredService<SnapshotChangeListener>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<SnapshotChangeListener>());
        services.AddSingleton<SnapshotWatchHub>();
        services.AddHostedService(sp => sp.GetRequiredService<SnapshotWatchHub>());

        // The routes are registered whether or not the generic mutations are exposed: a machine's own surface and
        // host code dispatch them through the mediator. Exposure is the TrainMutationExposure registered above.
        services.AddScopedTraxRoute<ISaveSnapshot, SaveSnapshot>();
        services.AddScopedTraxRoute<IAdvanceSnapshot, AdvanceSnapshot>();
        services.AddScopedTraxRoute<ILoadSnapshot, LoadSnapshot>();
        services.AddScopedTraxRoute<ISendSnapshot, SendSnapshot>();
        services.AddScopedTraxRoute<IStartSnapshot, StartSnapshot>();
        services.AddScopedTraxRoute<IDiscardSnapshot, DiscardSnapshot>();
    }

    // The six generic trains and how each is described when the host serves it.
    private static readonly (Type Train, string Description)[] GenericMutations =
    [
        (typeof(SaveSnapshot), "Autosave: validate a client snapshot and persist it (soft path)."),
        (
            typeof(AdvanceSnapshot),
            "Advance: re-drive a stored draft by one trigger, server-side (authoritative path)."
        ),
        (typeof(LoadSnapshot), "Load: resume the caller's stored draft."),
        (
            typeof(SendSnapshot),
            "Send: run a machine's one irreversible effect exactly once (state-gated + idempotent)."
        ),
        (
            typeof(StartSnapshot),
            "Start: create an instance from the machine's declared start, under an id the server mints."
        ),
        (
            typeof(DiscardSnapshot),
            "Discard: delete one of the caller's instances, cancelling a run its state invoked first."
        ),
    ];
}

/// <summary>
/// The IR hash (<see cref="IMachine.SchemaHash"/>) of each registered machine by name, which the store writes to a
/// row's <c>ir_hash</c>. Null for a machine with no exportable IR, and for a name no registered machine has.
/// </summary>
internal sealed class MachineIrHashes(IEnumerable<IMachine> machines)
{
    private readonly Lazy<IReadOnlyDictionary<string, IMachine>> _byName = new(() =>
        machines
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    /// <summary>The IR hash of the machine named <paramref name="machine"/>, or null.</summary>
    public string? Of(string machine) =>
        _byName.Value.TryGetValue(machine, out var found) ? found.SchemaHash : null;
}

/// <summary>
/// What a write that does not say does to a row's timer, by machine, state and context, from the registered machines'
/// timers (see <see cref="EfSnapshotStore.Timers"/>). Null for a name no registered machine has, and for a duration the
/// context cannot give.
/// </summary>
internal sealed class MachineTimerLookup(IEnumerable<IMachine> machines)
{
    private readonly Lazy<IReadOnlyDictionary<string, IMachineInternals>> _byName = new(() =>
        machines
            .OfType<IMachineInternals>()
            .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    /// <summary>The timer a write in <paramref name="state"/> keeps from entry, none, or null when it cannot tell.</summary>
    public SnapshotTimer? Of(string machine, string state, JsonObject context) =>
        _byName.Value.TryGetValue(machine, out var found)
            ? found.TimerFor(state, context, restart: false).Timer
            : null;
}
