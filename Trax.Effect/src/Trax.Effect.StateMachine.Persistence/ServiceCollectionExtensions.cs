using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Discover every <see cref="Machine{TState,TTrigger}"/> in the given assemblies and wire the whole
    /// subsystem, the store, the effect-claim ledger, the exactly-once runner, the machine registry, and
    /// the four generic <c>stateMachine</c> mutations, in one call. No per-machine registration, and no
    /// effect wiring in the composition root: each machine declares its committed states and its
    /// irreversible effect inline, and this reads them off the fluent build.
    ///
    /// <para>The host still binds two things a machine can't know: an <see cref="ISnapshotPrincipal"/>
    /// (mapping its auth to a user key) and each effect implementation the machines reference. It also
    /// adds <see cref="Mutations.StateMachineMutations.Assembly"/> to its <c>AddMediator(...)</c> scan so
    /// Trax can route the four mutations by input type.</para>
    /// </summary>
    public static IServiceCollection AddTraxStateMachines(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) => services.AddTraxStateMachines(_ => { }, assemblies);

    /// <summary>
    /// As <see cref="AddTraxStateMachines(IServiceCollection, Assembly[])"/>, with host-level options (see
    /// <see cref="StateMachineOptions"/>) such as the draft TTL. Example:
    /// <c>services.AddTraxStateMachines(o =&gt; o.DraftTtl = TimeSpan.FromDays(30), typeof(Program).Assembly)</c>.
    /// </summary>
    public static IServiceCollection AddTraxStateMachines(
        this IServiceCollection services,
        Action<StateMachineOptions> configure,
        params Assembly[] assemblies
    )
    {
        var options = new StateMachineOptions();
        configure(options);
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
                "AddTraxStateMachines(...) found no machines. Pass the assemblies that contain your "
                    + "Machine<TState, TTrigger> subclasses, e.g. services.AddTraxStateMachines(typeof(Program).Assembly)."
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

        return services;
    }
}
