using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.Dashboard.Serving;

/// <summary>
/// Puts the dashboard's security headers on every response to a request routed to its endpoint,
/// including the ones the endpoint never writes: a 401 or 403 from the authorization middleware,
/// or a sign-in redirect from a cookie scheme's challenge.
/// </summary>
/// <remarks>
/// Those refusals are answered before the endpoint runs, and <c>UseTraxApiDashboard()</c> is
/// called after the host has fixed its middleware order, so no middleware of the dashboard's can
/// sit in front of authorization. Routing can: it announces the endpoint it matched on the host's
/// <see cref="DiagnosticListener"/> (<see cref="EndpointMatched"/>) before any middleware after it
/// runs. For a request matched to this dashboard's endpoint, and no other, the headers are set
/// when the response starts, whatever answered it.
/// </remarks>
internal sealed class DashboardResponseHeaders(DashboardEndpointMetadata endpoint)
    : IObserver<KeyValuePair<string, object?>>
{
    /// <summary>The event endpoint routing writes when it has matched a request to an endpoint.</summary>
    internal const string EndpointMatched = "Microsoft.AspNetCore.Routing.EndpointMatched";

    private static readonly Func<object, Task> SetHeaders = state =>
    {
        DashboardEndpoint.SetSecurityHeaders((HttpContext)state);
        return Task.CompletedTask;
    };

    /// <summary>
    /// Subscribes for the dashboard endpoint carrying <paramref name="endpoint"/>, on the listener
    /// endpoint routing writes to. The subscription lives as long as the host's container, which
    /// disposes the listener.
    /// </summary>
    internal static void Subscribe(
        DiagnosticListener listener,
        DashboardEndpointMetadata endpoint
    ) =>
        listener.Subscribe(new DashboardResponseHeaders(endpoint), name => name == EndpointMatched);

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (
            value.Key == EndpointMatched
            && value.Value is HttpContext context
            && ReferenceEquals(
                context.GetEndpoint()?.Metadata.GetMetadata<DashboardEndpointMetadata>(),
                endpoint
            )
        )
            context.Response.OnStarting(SetHeaders, context);
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}

/// <summary>
/// Marks the endpoint one <c>UseTraxApiDashboard()</c> call mapped, with the posture it was given.
/// </summary>
internal sealed class DashboardEndpointMetadata(bool anonymousShell)
{
    /// <summary>Whether the call was given <c>AllowAnonymousShell()</c>.</summary>
    internal bool AnonymousShell { get; } = anonymousShell;
}
