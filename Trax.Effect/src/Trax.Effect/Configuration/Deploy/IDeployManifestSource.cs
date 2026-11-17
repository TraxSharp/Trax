using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Adds what only the built host knows (its endpoints, its authentication schemes) to the deploy manifest. Registered
/// by the packages that map endpoints.
/// </summary>
internal interface IDeployManifestSource
{
    /// <param name="manifest">The manifest being built.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="host">The host itself, which on a web host is where its endpoints are mapped.</param>
    /// <param name="cancellationToken">Stops the export.</param>
    Task DescribeAsync(
        DeployManifest manifest,
        IServiceProvider services,
        IHost? host,
        CancellationToken cancellationToken
    );
}
