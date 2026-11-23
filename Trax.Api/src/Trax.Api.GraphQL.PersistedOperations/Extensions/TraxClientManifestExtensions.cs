using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Extensions;

namespace Trax.Api.GraphQL.PersistedOperations.Extensions;

/// <summary>
/// Runs a Trax host, or exports its client manifest instead of serving. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest">SDK reference</seealso>
public static class TraxClientManifestExtensions
{
    /// <summary>
    /// Runs the host, as <c>app.RunAsync()</c> does, unless the process was started with
    /// <c>--trax-migrate</c>, which migrates and exits, or <c>--trax-export-deploy &lt;path&gt;</c>, which writes the
    /// deploy manifest and exits (both <see cref="TraxDeployExtensions.RunTraxAsync"/>), or with
    /// <c>--trax-export-client &lt;path&gt;</c>. Then it runs the host's startup checks, writes the client manifest
    /// that <c>trax client</c> reads to the path, and returns without serving: no hosted service starts, nothing
    /// listens, and no database is opened (<see cref="TraxClientExport"/>). A check that refuses the host refuses the
    /// export too. Call it in place of <c>app.Run()</c>, and do any seeding a host does in a hosted service or after
    /// <see cref="TraxClientExport.Requested"/> is checked, so an export reaches none of it.
    /// </summary>
    /// <param name="app">The built host.</param>
    /// <param name="args">
    /// The process's arguments. A flag counts when it is in these or in the process's own command line; null reads
    /// only the command line.
    /// </param>
    public static async Task RunTraxAsync(this WebApplication app, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var path = (args is null ? null : TraxClientExport.PathIn(args)) ?? TraxClientExport.Path;
        if (path is null)
        {
            // Serves, or migrates or exports the deploy manifest and exits (TraxDeployExtensions).
            await TraxDeployExtensions
                .RunTraxAsync(app, args, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        await ClientManifestExport
            .WriteAsync(app.Services, path, CancellationToken.None)
            .ConfigureAwait(false);
        app.Services.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(TraxClientManifestExtensions))
            .LogInformation("Wrote the client manifest to {Path}.", Path.GetFullPath(path));
    }
}
