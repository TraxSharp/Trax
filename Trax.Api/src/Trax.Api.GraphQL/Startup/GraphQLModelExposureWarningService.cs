using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Warns at host start when a GraphQL schema exposes <c>AddDbContext</c>-driven
/// model queries that are NOT individually gated by <c>[TraxAuthorize]</c>. The
/// ungated model surface relies entirely on endpoint-level authorization; if the
/// endpoint is anonymous, every authenticated caller can read every ungated
/// registered entity.
/// </summary>
/// <remarks>
/// Emits at <see cref="LogLevel.Warning"/> only — it is a reminder, not a fatal
/// misconfiguration. Teams that intentionally ship public model-query endpoints
/// can ignore the log or filter it out. Teams that did not intend public reads
/// should either add <c>RequireAuthorization(...)</c> to the endpoint or attach
/// <c>[TraxAuthorize]</c> to the sensitive entity classes so the directive runs
/// at type level (and is enforced even when the entity is reached transitively
/// via a navigation property on an ungated parent).
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

        // Entities marked [TraxAllowAnonymous] are intentionally opened, not
        // ungated by omission. Excluding them keeps the warning focused on
        // entities the developer may have forgotten to gate.
        var ungated = configuration
            .ModelRegistrations.Where(r => r.AuthorizeAttributes.Count == 0 && !r.AllowAnonymous)
            .Count();

        if (ungated == 0)
            return Task.CompletedTask;

        logger.LogWarning(
            "Trax GraphQL: {Ungated} of {Total} model query registration(s) carry no "
                + "[TraxAuthorize]. Ungated model queries are exposed to every caller that "
                + "reaches the endpoint. Either gate the endpoint via "
                + "`UseTraxGraphQL(configure: e => e.RequireAuthorization(...))`, attach "
                + "[TraxAuthorize] to the sensitive entity classes (which enforces at type "
                + "level, including transitively through navigation properties), or accept "
                + "the public-read posture intentionally.",
            ungated,
            configuration.ModelRegistrations.Count
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
