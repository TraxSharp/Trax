using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.FeatureDbConfigurator;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Registers state-machine persistence as a first-class Trax subsystem inside <c>AddTrax</c>. One call
/// discovers the machines, wires the store, the effect-claim ledger, the exactly-once runner, the registry,
/// and the four generic <c>stateMachine</c> mutations, AUTO-registers the <see cref="SnapshotDbContext"/>
/// against the provider the host already chose, and contributes the mutations to the mediator scan. The host
/// writes one call and nothing else: no <c>AddDbContext&lt;SnapshotDbContext&gt;</c>, no naming of the
/// mutations' assembly.
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

        // Auto-register the SnapshotDbContext against the provider the host configured in AddEffects, via the
        // ITraxFeatureDbConfigurator each UseXxx registers. The host never writes AddDbContext.
        builder.ServiceCollection.AddDbContext<SnapshotDbContext>(
            (sp, options) => sp.GetRequiredService<ITraxFeatureDbConfigurator>().Configure(options)
        );

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

        services.AddScoped<ISnapshotStore, EfSnapshotStore>();
        services.AddScoped<IEffectClaimStore, EfEffectClaimStore>();
        services.AddScoped<IdempotentEffect>();
        services.AddScoped<ISnapshotMachineRegistry, SnapshotMachineRegistry>();

        services.AddScopedTraxRoute<ISaveSnapshot, SaveSnapshot>();
        services.AddScopedTraxRoute<IAdvanceSnapshot, AdvanceSnapshot>();
        services.AddScopedTraxRoute<ILoadSnapshot, LoadSnapshot>();
        services.AddScopedTraxRoute<ISendSnapshot, SendSnapshot>();
    }
}
