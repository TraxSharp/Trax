using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.Dashboard.Serving;

/// <summary>
/// Marks the endpoint one <c>UseTraxApiDashboard()</c> call mapped, with the posture it was given.
/// </summary>
internal sealed class DashboardEndpointMetadata(bool anonymousShell)
{
    /// <summary>Whether the call was given <c>AllowAnonymousShell()</c>.</summary>
    internal bool AnonymousShell { get; } = anonymousShell;
}
