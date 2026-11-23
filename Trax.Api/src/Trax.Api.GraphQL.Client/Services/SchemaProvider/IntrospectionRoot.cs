namespace Trax.Api.GraphQL.Client;

/// <summary>
/// POCO model of the GraphQL introspection response shape (per the GraphQL spec, "Introspection"
/// section). Property naming follows the JSON-cased form on the wire; <see cref="System.Text.Json"/>
/// is configured with <c>PropertyNameCaseInsensitive = true</c> to bind these.
/// </summary>
internal sealed class IntrospectionRoot
{
    public IntrospectionSchema? __Schema { get; set; }
}
