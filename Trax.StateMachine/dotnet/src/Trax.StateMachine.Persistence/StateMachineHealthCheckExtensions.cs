using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Registers the state-machine runtime self-check with ASP.NET Core health checks.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddHealthChecks().AddTraxStateMachineSelfCheck();
/// </code>
/// </example>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/add-trax-state-machine-self-check">SDK reference</seealso>
public static class StateMachineHealthCheckExtensions
{
    /// <summary>
    /// Register the state-machine self-check. Add it wherever the host builds its health checks; it resolves
    /// every <see cref="IMachine"/> that <c>AddStateMachines</c> discovered, so it needs no configuration and
    /// picks up a new machine automatically.
    /// </summary>
    public static IHealthChecksBuilder AddTraxStateMachineSelfCheck(
        this IHealthChecksBuilder builder,
        string name = "state-machines"
    ) => builder.AddCheck<StateMachineSelfCheckHealthCheck>(name);
}
