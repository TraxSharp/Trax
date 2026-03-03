using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.Rest.Endpoints;

namespace Trax.Api.Rest.Extensions;

public static class RestApiServiceExtensions
{
    /// <summary>
    /// Registers Trax REST API services.
    /// </summary>
    public static IServiceCollection AddTraxRestApi(this IServiceCollection services)
    {
        services.AddTraxApi();
        return services;
    }

    /// <summary>
    /// Maps all Trax REST API endpoints under the specified route prefix.
    /// Use the optional <paramref name="configure"/> callback to apply endpoint conventions
    /// such as authorization, rate limiting, or CORS to all Trax REST endpoints.
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseTraxRestApi(configure: group => group
    ///     .RequireAuthorization("AdminPolicy")
    ///     .RequireRateLimiting("fixed"));
    /// </code>
    /// </example>
    public static WebApplication UseTraxRestApi(
        this WebApplication app,
        string routePrefix = "/trax/api",
        Action<RouteGroupBuilder>? configure = null
    )
    {
        var group = app.MapGroup(routePrefix);
        configure?.Invoke(group);
        group.MapTrainEndpoints();
        group.MapSchedulerEndpoints();
        group.MapManifestEndpoints();
        group.MapManifestGroupEndpoints();
        group.MapExecutionEndpoints();
        return app;
    }
}
