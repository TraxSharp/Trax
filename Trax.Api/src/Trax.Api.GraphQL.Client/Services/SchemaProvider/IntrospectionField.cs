namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionField
{
    public string Name { get; set; } = "";
    public List<IntrospectionInputValue>? Args { get; set; }
    public required IntrospectionTypeRef Type { get; set; }
    public bool IsDeprecated { get; set; }
    public string? DeprecationReason { get; set; }
}
