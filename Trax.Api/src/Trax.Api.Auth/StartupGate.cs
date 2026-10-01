using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth;

/// <summary>
/// A check that refuses to start a misconfigured host (docs/adr/0001-a-misconfigured-host-fails-at-startup.md).
/// </summary>
/// <remarks>
/// It checks in <see cref="StartingAsync"/>, which the host finishes for every hosted service
/// before it calls any <c>StartAsync</c>, so a refusal stops the host before Kestrel binds and
/// before a worker starts, even under <c>HostOptions.ServicesStartConcurrently</c>, where every
/// <c>StartAsync</c> begins at once. Something that starts hosted services itself and calls only
/// <c>StartAsync</c>, such as a custom <c>IHost</c> or a test harness, gets the same check from
/// <see cref="StartAsync"/>, so the gate does not open because a lifecycle step was skipped.
/// </remarks>
internal abstract class StartupGate : IHostedLifecycleService
{
    private bool _checked;

    /// <summary>Throws when the host must not start.</summary>
    protected abstract Task CheckAsync(CancellationToken cancellationToken);

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        return CheckAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
