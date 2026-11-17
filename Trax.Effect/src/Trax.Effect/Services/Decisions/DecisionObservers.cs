using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Extensions;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// Registers decision observers so that every one of them is told, through
/// <see cref="CompositeDecisionObserver"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal static class DecisionObservers
{
    /// <summary>
    /// Adds an observer made by <paramref name="factory"/> beside every other one, once per
    /// <paramref name="tag"/>. Any <see cref="IDecisionObserver"/> registered directly before this
    /// call is folded into the composite with its own lifetime.
    /// </summary>
    public static void Add(
        IServiceCollection services,
        Type tag,
        Func<IServiceProvider, IDecisionObserver> factory,
        ServiceLifetime lifetime
    )
    {
        FoldExisting(services);

        if (
            services.Any(d =>
                d.ImplementationInstance is DecisionObserverPart { } p && p.Tag == tag
            )
        )
            return;

        var key = new object();
        services.Add(
            new ServiceDescriptor(typeof(IDecisionObserver), key, (sp, _) => factory(sp), lifetime)
        );
        services.AddSingleton(new DecisionObserverPart(key, tag));
    }

    private static void FoldExisting(IServiceCollection services)
    {
        foreach (
            var descriptor in services
                .Where(d =>
                    d.ServiceType == typeof(IDecisionObserver)
                    && !d.IsKeyedService
                    && d.ImplementationType != typeof(CompositeDecisionObserver)
                )
                .ToList()
        )
        {
            var key = new object();
            services.Remove(descriptor);
            services.Add(Keyed(descriptor, key));
            services.AddSingleton(
                new DecisionObserverPart(key, Tag: null, KnownBestEffort(descriptor))
            );
        }

        if (
            !services.Any(d =>
                d.ServiceType == typeof(IDecisionObserver)
                && !d.IsKeyedService
                && d.ImplementationType == typeof(CompositeDecisionObserver)
            )
        )
        {
            services.AddTransient<IDecisionObserver, CompositeDecisionObserver>();
            services.AddSingleton<DecisionObserverCheck>();
            services.AddHostedService(sp => sp.GetRequiredService<DecisionObserverCheck>());
        }
    }

    /// <summary>
    /// Whether the observer is best effort as registered, without building it: an instance that is
    /// not required, or a type that leaves <see cref="IDecisionObserver.Required"/> to its default.
    /// A factory's observer cannot be told without calling it, so it is not.
    /// </summary>
    private static bool KnownBestEffort(ServiceDescriptor descriptor)
    {
        try
        {
            return descriptor switch
            {
                { ImplementationInstance: IDecisionObserver instance } => !instance.Required,
                { ImplementationType: { } type } => !ImplementsRequired(type),
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool ImplementsRequired(Type type)
    {
        var getter = typeof(IDecisionObserver)
            .GetProperty(nameof(IDecisionObserver.Required))
            .NotNull()
            .GetMethod.NotNull();
        var map = type.GetInterfaceMap(typeof(IDecisionObserver));
        var index = Array.IndexOf(map.InterfaceMethods, getter);
        return index < 0 || map.TargetMethods[index].DeclaringType != typeof(IDecisionObserver);
    }

    private static ServiceDescriptor Keyed(ServiceDescriptor descriptor, object key) =>
        descriptor switch
        {
            { ImplementationInstance: { } instance } => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                instance
            ),
            { ImplementationFactory: { } factory } => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                (sp, _) => factory(sp),
                descriptor.Lifetime
            ),
            _ => new ServiceDescriptor(
                typeof(IDecisionObserver),
                key,
                descriptor.ImplementationType.NotNull(),
                descriptor.Lifetime
            ),
        };
}
