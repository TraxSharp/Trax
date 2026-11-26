using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Services.DeployMigration;
using Trax.Effect.Data.Services.Drain;

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
    /// <see cref="DeployManifest"/> to the path and returns without serving; with <c>--trax-drain [timeout]</c>, which
    /// stops every host sharing the database from starting new work and waits for what is busy (see
    /// <see cref="TraxDrain"/>), or <c>--trax-resume</c>, which ends that; or with <c>--trax-migrate</c>. Then
    /// Trax's schema has already been migrated while services were registered; this applies the migrations of every
    /// context named with <see cref="MigrateOnDeploy{TContext}"/>, in the order they were named, logs what each did,
    /// and returns without starting the host, so no hosted service runs and nothing listens.
    /// </summary>
    /// <param name="host">The built host.</param>
    /// <param name="args">
    /// The process's arguments. A flag counts when it is in these or in the process's own command line, so a host
    /// handed fewer arguments than it was started with still exits rather than serving; null reads only the command
    /// line.
    /// </param>
    /// <param name="cancellationToken">Stops the host, or the migration.</param>
    public static async Task RunTraxAsync(
        this IHost host,
        string[]? args = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        await host.RunTraxAsync(
                args,
                TraxDeployExport.Path,
                TraxMigrate.Requested,
                cancellationToken,
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(TraxDrain.ForceVariable),
                Environment.GetEnvironmentVariable(TraxDrain.HoldVariable)
            )
            .ConfigureAwait(false);
    }

    /// <param name="host">The built host.</param>
    /// <param name="args">The arguments the host was handed; null when it was handed none.</param>
    /// <param name="processExportPath">The path the process's own <c>--trax-export-deploy</c> names, or null.</param>
    /// <param name="processMigrates">Whether the process was started with <c>--trax-migrate</c>.</param>
    /// <param name="cancellationToken">Stops the host, or the migration.</param>
    /// <param name="processArgs">The process's own command line, read for <c>--trax-drain</c> and <c>--trax-resume</c>.</param>
    /// <param name="forceVariable">The value of <c>TRAX_DRAIN_FORCE</c>.</param>
    /// <param name="holdVariable">The value of <c>TRAX_DRAIN_HOLD</c>.</param>
    internal static async Task RunTraxAsync(
        this IHost host,
        string[]? args,
        string? processExportPath,
        bool processMigrates,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? processArgs = null,
        string? forceVariable = null,
        string? holdVariable = null
    )
    {
        var exportPath = (args is null ? null : TraxDeployExport.PathIn(args)) ?? processExportPath;
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

        var drainArgs =
            args is not null && TraxDrain.RequestedIn(args) ? args
            : processArgs is not null && TraxDrain.RequestedIn(processArgs) ? processArgs
            : null;
        if (drainArgs is not null)
        {
            if (TraxDrain.TimeoutIn(drainArgs) is { } timeout)
                await DrainCommand
                    .DrainAsync(
                        host.Services,
                        timeout,
                        TraxDrain.HoldIn(holdVariable),
                        TraxDrain.ForceRequested(forceVariable),
                        DrainCommand.Settle,
                        DrainCommand.Poll,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            else
                await DrainCommand
                    .ResumeAsync(host.Services, cancellationToken)
                    .ConfigureAwait(false);
            return;
        }

        var migrate = processMigrates || (args is not null && TraxMigrate.RequestedIn(args));
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
