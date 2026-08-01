namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Host-level options for the state-machine subsystem, configured through the
/// <see cref="ServiceCollectionExtensions.AddTraxStateMachines(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{StateMachineOptions}, System.Reflection.Assembly[])"/>
/// overload and read by the machine registry when it builds a per-machine draft service.
/// </summary>
public sealed class StateMachineOptions
{
    /// <summary>
    /// How long a draft survives without activity before the next <c>Load</c> discards it and the user
    /// starts fresh (a sliding window on the row's last update). The stale row is deleted, so an abandoned
    /// or long-completed draft can't linger or block a new one. <c>null</c> (the default) never expires a
    /// draft. Recommended: 7-30 days for a form-style flow.
    /// </summary>
    public TimeSpan? DraftTtl { get; set; }
}
