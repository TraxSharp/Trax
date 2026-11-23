namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionTypeRef
{
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public IntrospectionTypeRef? OfType { get; set; }
}
