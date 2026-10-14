using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Services.HealthCheck;

namespace Trax.Api.Extensions;

/// <summary>
/// Adds the Trax health check to ASP.NET Core health checks.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/add-trax-health-check">SDK reference</seealso>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds the Trax health check that reports queue depth, in-progress count,
    /// recent failures, and unresolved dead letters.
    /// </summary>
    public static IHealthChecksBuilder AddTraxHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "trax",
        params string[] tags
    )
    {
        return builder.AddCheck<TraxHealthCheck>(name, tags: tags);
    }
}
