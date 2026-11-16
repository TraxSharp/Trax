using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Services.DeployMigration;

namespace Trax.Effect.Data.Extensions;

/// <summary>
/// What a host does when a deployment starts it to migrate the database (<c>--trax-migrate</c>) rather than to serve.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/trax-migrate">SDK reference</seealso>
public static class TraxDeployExtensions
{
    /// <summary>
    /// Has a process started with <c>--trax-migrate</c> bring <typeparamref name="TContext"/> up to date as well as
    /// Trax's own schema: its pending EF migrations are applied, or, when it has none, its missing tables are created
    /// the way <c>EnsureSchemaCreatedAsync</c> creates them. A host that serves does nothing with it.
    /// </summary>
    /// <typeparam name="TContext">
    /// A context registered with <c>AddDbContextFactory</c> or <c>AddDomainDataContext</c>.
    /// </typeparam>
    /// <param name="builder">The effect builder, after its data provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TraxEffectBuilderWithData MigrateOnDeploy<TContext>(
        this TraxEffectBuilderWithData builder
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ServiceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeployMigration, DeployMigration<TContext>>()
        );
        return builder;
    }

    /// <summary>
    /// Runs the host, as <c>host.RunAsync()</c> does, unless the process was started with
    /// <c>--trax-export-deploy &lt;path&gt;</c>, which runs the host's startup checks, writes its
    /// <see cref="DeployManifest"/> to the path and returns without serving, or with <c>--trax-migrate</c>. Then
    /// Trax's schema has already been migrated while services were registered; this applies the migrations of every
    /// context named with <see cref="MigrateOnDeploy{TContext}"/>, in the order they were named, logs what each did,
    /// and returns without starting the host, so no hosted service runs and nothing listens.
    /// </summary>
    /// <param name="host">The built host.</param>
    /// <param name="args">The process's arguments; null reads them from the command line.</param>
    /// <param name="cancellationToken">Stops the host, or the migration.</param>
    public static async Task RunTraxAsync(
        this IHost host,
        string[]? args = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        var exportPath = args is null ? TraxDeployExport.Path : TraxDeployExport.PathIn(args);
        if (exportPath is not null)
        {
            await DeployManifestExport
                .WriteAsync(host, exportPath, cancellationToken)
                .ConfigureAwait(false);
            host.Services.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(TraxDeployExtensions))
                .LogInformation(
                    "Wrote the deploy manifest to {Path}.",
                    Path.GetFullPath(exportPath)
                );
            return;
        }

        var migrate = args is null ? TraxMigrate.Requested : TraxMigrate.RequestedIn(args);
        if (!migrate)
        {
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var logger = host
            .Services.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(TraxDeployExtensions));
        foreach (var migration in host.Services.GetServices<IDeployMigration>())
        {
            var result = await migration
                .RunAsync(host.Services, cancellationToken)
                .ConfigureAwait(false);
            logger?.LogInformation("{Context}: {Result}.", migration.ContextName, result);
        }
        logger?.LogInformation("Migration finished; this process does not serve.");
    }
}
