using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.GraphQL.Deploy;

/// <summary>
/// Adds what only the built web host knows to the deploy manifest: the paths <c>UseTraxGraphQL</c>,
/// <c>UseTraxApiDashboard</c> and <c>MapHealthChecks</c> mapped, and the authentication schemes it registers. A host
/// that maps the Trax GraphQL endpoint runs the <c>api</c> role.
/// </summary>
internal sealed class ApiDeployManifestSource : IDeployManifestSource
{
    // MapHealthChecks names its endpoint this unless the host renames it.
    private const string HealthChecksDisplayName = "Health checks";

    public async Task DescribeAsync(
        DeployManifest manifest,
        IServiceProvider services,
        IHost? host,
        CancellationToken cancellationToken
    )
    {
        var graphQL = services.GetService<GraphQLConfiguration>();
        if (graphQL?.MappedRoutePrefix is { } graphQLPath)
        {
            manifest.Endpoints.GraphQL = graphQLPath;
            manifest.Roles.Add("api");
        }
        manifest.Endpoints.Console = graphQL?.MappedConsolePath;

        // A web application holds the endpoints it mapped itself until it starts; the container's data source covers
        // a host that maps them in UseEndpoints.
        var endpoints = host is IEndpointRouteBuilder routes
            ? routes.DataSources.SelectMany(d => d.Endpoints)
            : services.GetService<EndpointDataSource>()?.Endpoints ?? [];
        manifest.Endpoints.Health = endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(e => e.DisplayName == HealthChecksDisplayName)
            ?.RoutePattern.RawText;

        if (services.GetService<IAuthenticationSchemeProvider>() is { } schemes)
            foreach (var scheme in await schemes.GetAllSchemesAsync().ConfigureAwait(false))
                manifest.Authentication.Add(
                    new DeployAuthenticationScheme
                    {
                        Name = scheme.Name,
                        Handler = scheme.HandlerType.Name,
                    }
                );
    }
}
