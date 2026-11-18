namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionInputValue
{
    public string Name { get; set; } = "";
    public IntrospectionTypeRef Type { get; set; } = null!;
    public string? DefaultValue { get; set; }
}
