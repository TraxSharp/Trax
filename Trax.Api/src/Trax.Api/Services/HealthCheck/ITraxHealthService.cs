using Trax.Api.DTOs;

namespace Trax.Api.Services.HealthCheck;

/// <summary>
/// Queries Trax system health metrics from the database.
/// Used by both the ASP.NET IHealthCheck and the GraphQL health query.
/// </summary>
public interface ITraxHealthService
{
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}
