using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Refuses to start a host whose schema has no <c>machines</c> root while a machine marked
/// <c>[TraxMachine]</c> is registered.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> decides whether the schema gets the <c>machines</c> root from the marked machines
/// registered so far by their type or as an instance (docs/adr/0002-reading-the-service-collection-is-order-dependent.md).
/// A marked machine registered through a factory, or after <c>AddTraxGraphQL()</c>, is seen only once the container
/// is built: when no other marked machine was seen, the root is missing and the machine would be silently unserved.
/// This names each one instead. When the root is there, every marked machine the container holds is served, however
/// it was registered.
/// </remarks>
internal sealed class MachineRegistrationOrderValidator(IEnumerable<IMachine> machines)
    : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        var missed = StateMachineOptions
            .Served(machines)
            .Select(m => $"'{m.Name}' ({m.GetType().FullName})")
            .Distinct()
            .ToList();

        if (missed.Count > 0)
            throw new InvalidOperationException(
                "These machines are marked [TraxMachine], but AddTraxGraphQL() saw none of them, so the schema has "
                    + "no machines root to serve them under: "
                    + string.Join(", ", missed)
                    + ". AddTraxGraphQL() reads the machines registered before it by their type or as an instance: "
                    + "register them before AddTraxGraphQL(), for example through AddStateMachines(...) inside "
                    + "AddTrax(...), and not through a factory."
            );

        return Task.CompletedTask;
    }
}
