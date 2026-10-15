using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

// The fakes as a host that serves them registers them: each a subclass marked [TraxMachine], since the attribute is
// the only thing that serves a machine. A host registers a fake's marked subclass in its place when it serves it, and
// the unmarked fake when it does not (E2EMachines). No host scans this assembly for machines, so neither is registered
// anywhere it is not named.

/// <summary><see cref="WizardMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedWizardMachine : WizardMachine;

/// <summary><see cref="MemoMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedMemoMachine : MemoMachine;

/// <summary><see cref="SnoozeMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedSnoozeMachine : SnoozeMachine;

/// <summary><see cref="ShelfMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedShelfMachine : ShelfMachine;

/// <summary><see cref="TicketMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedTicketMachine : TicketMachine;

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
    /// <paramref name="served"/> is registered as its marked subclass instead, so the host serves exactly those.
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
    }

    private static Type? MarkedOf(Type machine) =>
        Concrete.SingleOrDefault(t =>
            t.BaseType == machine && t.IsDefined(typeof(TraxMachineAttribute), inherit: false)
        );
}
