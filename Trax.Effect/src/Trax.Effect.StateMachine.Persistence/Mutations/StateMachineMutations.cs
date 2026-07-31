using System.Reflection;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

/// <summary>
/// The four generic <c>stateMachine</c> mutation trains ship in this package, not the host's assembly.
/// Trax routes a train by its input type through an assembly-scanned registry, so a host adds this
/// assembly to its mediator scan for the mutations to execute:
///
/// <code>
/// services.AddTrax(trax => trax
///     .AddEffects(e => e.UsePostgres(cs).AddJson())
///     .AddMediator(typeof(Program).Assembly, StateMachineMutations.Assembly));
/// services.AddTraxStateMachines(typeof(Program).Assembly);
/// </code>
/// </summary>
public static class StateMachineMutations
{
    /// <summary>The assembly holding the four generic <c>stateMachine</c> mutation trains.</summary>
    public static Assembly Assembly => typeof(SaveSnapshot).Assembly;
}
