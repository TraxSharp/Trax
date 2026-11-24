using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Refuses to start a host that was started with <c>--trax-drain</c> or <c>--trax-resume</c> and is serving instead.
/// Such a process sets or ends the drain and exits through <c>RunTraxAsync</c>; one that reached the host's start
/// would run a scheduler beside the hosts it is draining, and that scheduler's startup would end the drain and fail
/// the runs still live on them.
/// </summary>
internal sealed class DrainServeGate : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        throw new InvalidOperationException(
            $"This process was started with {TraxDrain.Argument} or {TraxDrain.ResumeArgument}, which sets or ends "
                + "the drain and exits, but the host is starting to serve. Call 'await app.RunTraxAsync(args)' in "
                + $"place of 'app.Run()' so the host exits once it is done, or start it without {TraxDrain.Argument} "
                + $"and {TraxDrain.ResumeArgument}."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
