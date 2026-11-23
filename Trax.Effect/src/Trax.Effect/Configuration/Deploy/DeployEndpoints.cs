using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>The paths a deployment routes to or probes. A path the host does not map is null.</summary>
public sealed class DeployEndpoints
{
    /// <summary>The Trax GraphQL endpoint, from <c>UseTraxGraphQL</c>.</summary>
    public string? GraphQL { get; set; }

    /// <summary>The health check, from <c>MapHealthChecks</c>.</summary>
    public string? Health { get; set; }

    /// <summary>The operations console, from <c>UseTraxApiDashboard</c>.</summary>
    public string? Console { get; set; }
}
