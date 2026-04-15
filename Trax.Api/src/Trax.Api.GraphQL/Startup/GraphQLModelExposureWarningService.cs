using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Warns at host start when a GraphQL schema exposes <c>AddDbContext</c>-driven
/// model queries without an endpoint-level authorization gate. The model-query
/// surface has no per-field authorization hook; anonymous clients can read every
/// registered entity unless the consumer gates the endpoint explicitly via the
/// <c>configure</c> callback on <c>UseTraxGraphQL</c>.
/// </summary>
/// <remarks>
/// Emits at <see cref="LogLevel.Warning"/> only — it is a reminder, not a fatal
/// misconfiguration. Teams that intentionally ship public model-query endpoints
/// can ignore the log or filter it out. Teams that did not intend public reads
/// should either add <c>RequireAuthorization(...)</c> to the endpoint or gate
/// specific queries with <c>[TraxAuthorize]</c> (when that surface lands).
/// </remarks>
internal sealed class GraphQLModelExposureWarningService(
    GraphQLConfiguration configuration,
    ILogger<GraphQLModelExposureWarningService> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.ModelRegistrations.Count == 0)
            return Task.CompletedTask;

        logger.LogWarning(
            "Trax GraphQL: {Count} model query registration(s) are active. "
                + "AddDbContext-backed model queries currently have no per-field authorization. "
                + "Unless you gate the endpoint via `UseTraxGraphQL(configure: e => e.RequireAuthorization(...))` "
                + "or accept that every authenticated caller can read every registered entity, "
                + "review this surface before shipping to production.",
            configuration.ModelRegistrations.Count
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
