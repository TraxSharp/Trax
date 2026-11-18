using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>Registers this assembly's machines on a host, serving the ones it names.</summary>
public static class E2EMachines
{
    private static readonly Type[] Concrete = typeof(E2EMachines)
        .Assembly.GetTypes()
        .Where(type =>
            type is { IsAbstract: false, IsClass: true, ContainsGenericParameters: false }
            && typeof(IMachine).IsAssignableFrom(type)
        )
        .ToArray();

    /// <summary>
    /// Registers every unmarked machine of this assembly as <see cref="IMachine"/>, except that each one in
    /// <paramref name="served"/> is registered as its marked subclass instead, so the host serves exactly those. An
    /// abstract machine is registered only when it is served.
    /// </summary>
    /// <exception cref="ArgumentException">A machine in <paramref name="served"/> has no marked subclass here.</exception>
    public static void Register(IServiceCollection services, IReadOnlyCollection<Type> served)
    {
        foreach (var type in served)
            if (MarkedOf(type) is null)
                throw new ArgumentException(
                    $"{type.Name} has no subclass marked [TraxMachine] in {typeof(E2EMachines).Assembly.GetName().Name}.",
                    nameof(served)
                );

        foreach (
            var type in Concrete.Where(t =>
                !t.IsDefined(typeof(TraxMachineAttribute), inherit: false)
            )
        )
            services.AddSingleton(typeof(IMachine), served.Contains(type) ? MarkedOf(type)! : type);

        // An abstract fake is registered only where a host serves it.
        foreach (var type in served.Where(t => t.IsAbstract))
            services.AddSingleton(typeof(IMachine), MarkedOf(type)!);
    }

    private static Type? MarkedOf(Type machine) =>
        Concrete.SingleOrDefault(t =>
            t.BaseType == machine && t.IsDefined(typeof(TraxMachineAttribute), inherit: false)
        );
}
