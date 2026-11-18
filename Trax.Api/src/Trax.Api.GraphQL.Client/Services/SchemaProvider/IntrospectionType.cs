namespace Trax.Api.GraphQL.Client;

// Note: the GraphQL introspection JSON also carries `description` fields, but the SDL
// builder doesn't emit descriptions (validation doesn't need them), so the model omits
// those properties — System.Text.Json ignores unknown JSON fields by default.
internal sealed class IntrospectionType
{
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public List<IntrospectionField>? Fields { get; set; }
    public List<IntrospectionInputValue>? InputFields { get; set; }
    public List<IntrospectionTypeRef>? Interfaces { get; set; }
    public List<IntrospectionEnumValue>? EnumValues { get; set; }
    public List<IntrospectionTypeRef>? PossibleTypes { get; set; }
}
