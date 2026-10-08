using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Registers state-machine persistence as a first-class Trax subsystem inside <c>AddTrax</c>. One call
/// discovers the machines, wires the store, the effect-claim ledger, the exactly-once runner, the registry,
/// and the four generic <c>stateMachine</c> mutations, and contributes the mutations to the mediator scan. The
/// stores reach their tables through the <see cref="IDataContext"/> the host's data provider registers, so the
/// host writes one call and nothing else: no <c>DbContext</c> of its own, no naming of the mutations' assembly.
/// </summary>
public static class StateMachinesBuilderExtensions
{
    /// <summary>
    /// Add state-machine persistence, discovering machines in <paramref name="assemblies"/>. Call after
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

        RegisterMachinesAndStores(builder.ServiceCollection, configure, assemblies);

        // Contribute the generic mutations' assembly so AddMediator scans it and the four mutations become
        // dispatchable, without the host naming the assembly.
        builder.Root.ContributedMediatorAssemblies.Add(StateMachineMutations.Assembly);

        return builder;
    }

    // Discover the machines and wire the store, the effect-claim ledger, the exactly-once runner, the machine
    // registry, and the four generic stateMachine mutation routes.
    private static void RegisterMachinesAndStores(
        IServiceCollection services,
        Action<StateMachineOptions>? configure,
        Assembly[] assemblies
    )
    {
        var options = new StateMachineOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var machineTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsAbstract: false, IsClass: true }
                && typeof(IMachine).IsAssignableFrom(type)
            )
            .Distinct()
            .ToList();

        if (machineTypes.Count == 0)
            throw new InvalidOperationException(
                "No state machines were found. Pass the assemblies that contain your "
                    + "Machine<TState, TTrigger> subclasses, e.g. trax.AddStateMachines(typeof(Program).Assembly)."
            );

        foreach (var type in machineTypes)
            services.AddSingleton(typeof(IMachine), type);

        // The stores use the request's data context, and the provider's dialect to read a unique violation as a
        // lost race. InMemory registers no dialect, so there a race throws rather than being misread.
        services.AddScoped<ISnapshotStore>(sp => new EfSnapshotStore(
            sp.GetRequiredService<IDataContext>(),
            sp.GetService<ISqlDialect>()
        ));
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
        ));
        services.AddScoped<InvokeOutbox>();
        services.AddScoped<IMachineInstances, MachineInstances>();
        services.AddScoped<IInvokedRunCancellation, InvokedRunCancellation>();
        services.AddHostedService<InvokesStartupValidator>();

        // An invoked run's outcome comes back to the state that queued it: the hook applies it on the host that ran
        // the train, and the reconciler, on every host that registers machines, sweeps for the ones the hook missed.
        // Both deliver through one conditional update on the token, so an outcome is applied once however many
        // hosts deliver it. The hook is not toggleable: the registry leaves an untracked factory enabled.
        services.AddScoped<InvokeOutcomeDelivery>();
        services.AddSingleton<InvokeOutcomeReconciler>();
        services.AddHostedService(sp => sp.GetRequiredService<InvokeOutcomeReconciler>());
        services.AddSingleton<ITrainLifecycleHookFactory, InvokeOutcomeHookFactory>();
        services.AddSingleton<IInvokedRunOutcomes, InvokedRunOutcomes>();

        services.AddScopedTraxRoute<ISaveSnapshot, SaveSnapshot>();
        services.AddScopedTraxRoute<IAdvanceSnapshot, AdvanceSnapshot>();
        services.AddScopedTraxRoute<ILoadSnapshot, LoadSnapshot>();
        services.AddScopedTraxRoute<ISendSnapshot, SendSnapshot>();
    }
}
