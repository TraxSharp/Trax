using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The runtime self-check as a health check: replay every registered machine's embedded differential corpus
/// through the engine this build actually shipped (<see cref="SnapshotSelfCheck.Run"/>). An empty result
/// means every machine still reproduces the behaviour its generated frontend twin was built from, so client
/// and server agree in production.
///
/// This is the drift a build-time test structurally cannot see: it runs against the deployed binary, so a
/// release whose engine no longer matches its committed corpus is Unhealthy rather than quietly wrong.
/// Machines that ship no corpus (raw-delegate machines) are skipped, not failed.
///
/// Infrastructure registered by <see cref="StateMachineHealthCheckExtensions.AddTraxStateMachineSelfCheck"/>;
/// not intended to be constructed directly.
/// </summary>
internal sealed class StateMachineSelfCheckHealthCheck(IEnumerable<IMachine> machines)
    : IHealthCheck
{
    /// <summary>
    /// Replays every machine's corpus synchronously on each call and reports Healthy when none drifts, or
    /// Unhealthy listing every diff. The replay cost grows with corpus size, so poll it at a modest interval.
    /// </summary>
    /// <param name="context">The health-check context (unused).</param>
    /// <param name="cancellationToken">Not observed; the replay runs to completion.</param>
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
                        + $"corpus:\n{string.Join('\n', diffs)}"
                )
        );
    }
}
