namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionField
{
    public string Name { get; set; } = "";
    public List<IntrospectionInputValue>? Args { get; set; }
    public IntrospectionTypeRef Type { get; set; } = null!;
    public bool IsDeprecated { get; set; }
    public string? DeprecationReason { get; set; }
}
