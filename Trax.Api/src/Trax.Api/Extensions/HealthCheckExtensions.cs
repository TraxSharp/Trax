using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Services.HealthCheck;

namespace Trax.Api.Extensions;

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
