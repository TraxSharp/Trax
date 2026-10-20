using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Effect.Data.Services.DeployMigration;

/// <summary>
/// Builds and writes a host's <see cref="DeployManifest"/>: what its registrations recorded (<see cref="DeployFact"/>)
/// and what the built host adds (<see cref="IDeployManifestSource"/>), after its startup checks pass.
/// </summary>
internal static class DeployManifestExport
{
    /// <summary>
    /// Runs every <see cref="ITraxStartupCheck"/> the host registers, then writes its manifest to
    /// <paramref name="path"/>. Starts no other hosted service, so nothing listens, sweeps or opens a database, and a
    /// check that refuses the host refuses the export.
    /// </summary>
    public static async Task WriteAsync(
        IHost host,
        string path,
        CancellationToken cancellationToken
    )
    {
        var services = host.Services;
        foreach (var check in services.GetServices<IHostedService>().OfType<ITraxStartupCheck>())
        {
            if (check is IHostedLifecycleService lifecycle)
                await lifecycle.StartingAsync(cancellationToken).ConfigureAwait(false);
            else
                await ((IHostedService)check).StartAsync(cancellationToken).ConfigureAwait(false);
        }

        var manifest = await BuildAsync(services, host, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, manifest.Write(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The manifest of the host <paramref name="services"/> belong to.</summary>
    public static async Task<DeployManifest> BuildAsync(
        IServiceProvider services,
        IHost? host,
        CancellationToken cancellationToken
    )
    {
        var facts = services.GetServices<DeployFact>().ToList();
        string[] Values(string kind) =>
            [
                .. facts
                    .Where(f => f.Kind == kind)
                    .Select(f => f.Value)
                    .Distinct()
                    .Order(StringComparer.Ordinal),
            ];

        var manifest = new DeployManifest
        {
            Roles = [.. Values(DeployFact.Role)],
            Transports = [.. Values(DeployFact.Transport)],
        };
        if (Values(DeployFact.Database) is [var provider, ..])
            manifest.Database = new DeployDatabase
            {
                Provider = provider,
                MigratesOnStart = Values(DeployFact.MigratesOnStart) is ["true"],
            };

        foreach (var source in services.GetServices<IDeployManifestSource>())
            await source
                .DescribeAsync(manifest, services, host, cancellationToken)
                .ConfigureAwait(false);

        manifest.Roles = [.. manifest.Roles.Distinct().Order(StringComparer.Ordinal)];
        manifest.Authentication =
        [
            .. manifest.Authentication.OrderBy(a => a.Name, StringComparer.Ordinal),
        ];
        return manifest;
    }
}
