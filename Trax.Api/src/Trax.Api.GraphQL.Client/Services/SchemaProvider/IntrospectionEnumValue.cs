namespace Trax.Api.GraphQL.Client;

internal sealed class IntrospectionEnumValue
{
    public string Name { get; set; } = "";
    public bool IsDeprecated { get; set; }
    public string? DeprecationReason { get; set; }
}
