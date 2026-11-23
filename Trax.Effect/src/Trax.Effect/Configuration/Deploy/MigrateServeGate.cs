using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Refuses to start a host that was started with <c>--trax-migrate</c> and is serving instead. A migrate process
/// exits once the schema is current, through <c>RunTraxAsync</c>; one that reaches the host's start would also start
/// its workers and listeners, beside the version it is migrating for.
/// </summary>
internal sealed class MigrateServeGate : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        throw new InvalidOperationException(
            $"This process was started with {TraxMigrate.Argument}, which migrates the database and exits, but the "
                + "host is starting to serve. Call 'await app.RunTraxAsync(args)' in place of 'app.Run()' so the host "
                + $"exits once the schema is current, or start it without {TraxMigrate.Argument}."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
