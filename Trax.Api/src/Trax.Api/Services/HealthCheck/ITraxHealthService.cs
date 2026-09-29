using System.ComponentModel;
using Trax.Api.DTOs;

namespace Trax.Api.Services.HealthCheck;

/// <summary>
/// Queries Trax system health metrics from the database.
/// Used by both the ASP.NET IHealthCheck and the GraphQL health query.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ITraxHealthService
{
    /// <summary>
    /// Computes the current <see cref="HealthStatus"/>. Runs its counts against the shared store on
    /// every call; nothing is cached.
    /// </summary>
    /// <param name="ct">Cancels the queries.</param>
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}
