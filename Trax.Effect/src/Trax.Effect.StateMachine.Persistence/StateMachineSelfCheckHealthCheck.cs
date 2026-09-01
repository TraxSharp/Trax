using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The runtime self-check as a health check: replay every registered machine's embedded differential corpus
/// through the engine this build actually shipped (<see cref="SnapshotSelfCheck.Run"/>). An empty result
/// means every machine still reproduces the behaviour its generated frontend twin was built from, so client
/// and server agree in production.
///
/// This is the drift a build-time test structurally cannot see: it runs against the deployed binary, so a
/// release whose engine no longer matches its committed corpus is Unhealthy rather than quietly wrong.
/// Machines that ship no corpus (raw-delegate machines) are skipped, not failed.
/// </summary>
public sealed class StateMachineSelfCheckHealthCheck(IEnumerable<IMachine> machines) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var diffs = SnapshotSelfCheck.Run(machines);
        return Task.FromResult(
            diffs.Count == 0
                ? HealthCheckResult.Healthy("Every state machine reproduces its committed corpus.")
                : HealthCheckResult.Unhealthy(
                    "State-machine self-check found drift between the deployed engine and the committed "
                        + $"corpus:\n{string.Join("\n", diffs)}"
                )
        );
    }
}

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
