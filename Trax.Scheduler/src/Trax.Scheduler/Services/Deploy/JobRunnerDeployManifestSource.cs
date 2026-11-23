using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Scheduler.Services.Deploy;

/// <summary>
/// Adds the <c>job-runner</c> role to the deploy manifest of a host that maps a runner endpoint
/// (<c>UseTraxJobRunner</c> or <c>UseTraxRunEndpoint</c>). Read from the endpoints rather than recorded by
/// <c>AddTraxJobRunner</c>, which a worker and an API-only host also call without receiving remote work.
/// </summary>
internal sealed class JobRunnerDeployManifestSource : IDeployManifestSource
{
    /// <summary>The role a host that maps a runner endpoint runs.</summary>
    public const string Role = "job-runner";

    public Task DescribeAsync(
        DeployManifest manifest,
        IServiceProvider services,
        IHost? host,
        CancellationToken cancellationToken
    )
    {
        // A web application holds the endpoints it mapped itself until it starts; the container's data source covers
        // a host that maps them in UseEndpoints.
        var endpoints = host is IEndpointRouteBuilder routes
            ? routes.DataSources.SelectMany(d => d.Endpoints)
            : services.GetService<EndpointDataSource>()?.Endpoints ?? [];
        if (endpoints.Any(e => e.Metadata.GetMetadata<JobRunnerEndpointMetadata>() is not null))
            manifest.Roles.Add(Role);
        return Task.CompletedTask;
    }
}
