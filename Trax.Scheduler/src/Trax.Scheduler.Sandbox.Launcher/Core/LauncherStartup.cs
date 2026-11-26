using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Backends;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// Runs before the launcher listens: the backend's own checks of its environment, then removes
/// whatever an earlier process left behind, then runs the canary. A throw here stops the host, so a launcher that cannot prove its sandboxes are
/// isolated never takes a request.
/// </summary>
internal sealed class LauncherStartup(
    ISandboxBackend backend,
    RuntimeCanary canary,
    ILogger<LauncherStartup> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await backend.PreflightAsync(cancellationToken).ConfigureAwait(false);
            await backend.SweepAsync(cancellationToken).ConfigureAwait(false);
            await canary.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeRefusedException ex)
        {
            logger.LogCritical(ex, "The sandbox launcher refuses to start: {Reason}", ex.Message);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
