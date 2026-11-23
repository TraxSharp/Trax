namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionInputValue
{
    public string Name { get; set; } = "";
    public required IntrospectionTypeRef Type { get; set; }
    public string? DefaultValue { get; set; }
}
