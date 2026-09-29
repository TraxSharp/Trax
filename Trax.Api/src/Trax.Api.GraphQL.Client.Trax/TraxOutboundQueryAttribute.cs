namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Marks an <see cref="IGenericGraphQLClientRequest"/> as an outbound dependency on a named
/// external GraphQL endpoint, so tooling can answer "which requests in this app call which
/// servers" without grepping the codebase.
///
/// The attribute is metadata-only: applying it does not change runtime behavior.
/// <see cref="OutboundQueryDiscovery"/> reads it; the dashboard does not display it today.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TraxOutboundQueryAttribute : Attribute
{
    /// <summary>Marks the request as a call to the named endpoint.</summary>
    /// <param name="endpoint">The endpoint's logical name, for example <c>PlayerService</c>. Not a URL.</param>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is null, empty or whitespace.</exception>
    public TraxOutboundQueryAttribute(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        Endpoint = endpoint;
    }

    /// <summary>
    /// Logical name of the external endpoint (e.g. "PlayerService", "BillingApi"). Not a URL -
    /// the URL is configured by the consuming app and varies per environment.
    /// </summary>
    public string Endpoint { get; }
}
