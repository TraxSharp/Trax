using System.Reflection;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

/// <summary>
/// The four generic <c>stateMachine</c> mutation trains ship in this package, not the host's assembly.
/// Trax routes a train by its input type through an assembly-scanned registry, so this
/// <see cref="Assembly"/> must be in the mediator scan for the mutations to execute.
/// <c>trax.AddStateMachines(...)</c> contributes it automatically, so a host never names it:
///
/// <code>
/// services.AddTrax(trax => trax
///     .AddEffects(e => e.UsePostgres(cs).AddJson())
///     .AddStateMachines(typeof(Program).Assembly)
///     .AddMediator(typeof(Program).Assembly));
/// </code>
/// </summary>
public static class StateMachineMutations
{
    /// <summary>The assembly holding the four generic <c>stateMachine</c> mutation trains.</summary>
    public static Assembly Assembly => typeof(SaveSnapshot).Assembly;
}
