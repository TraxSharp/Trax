using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.ClientExport;

/// <summary>
/// Refuses to start a host that was started to export its client manifest and is serving instead. Its data provider
/// skipped the migration for the export, so serving would answer on an unmigrated database. An export runs only the
/// <see cref="ITraxStartupCheck"/> services, and this is not one, so it fires only for a host that serves.
/// </summary>
internal sealed class ClientExportServeGate : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        throw new InvalidOperationException(
            $"This process was started with {TraxClientExport.Argument}, so its database was not migrated, but the "
                + "host is starting to serve. Call 'await app.RunTraxAsync(args)' in place of 'app.Run()' so the "
                + $"export is written and the host exits, or start it without {TraxClientExport.Argument}."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
