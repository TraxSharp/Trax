namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Marks an <see cref="IGenericGraphQLClientRequest"/> as an outbound dependency on a named
/// external GraphQL endpoint. Surfaced by the dashboard so operators can answer "which trains
/// in this app issue queries against which servers" without grepping the codebase.
///
/// The attribute is metadata-only: applying it does not change runtime behavior. Discovery
/// services walk the assembly looking for it and build the outbound-dependency graph.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TraxOutboundQueryAttribute : Attribute
{
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
