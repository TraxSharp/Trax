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

internal sealed class IntrospectionSchema
{
    public IntrospectionTypeRef QueryType { get; set; } = null!;
    public IntrospectionTypeRef? MutationType { get; set; }
    public IntrospectionTypeRef? SubscriptionType { get; set; }
    public List<IntrospectionType> Types { get; set; } = new();
    public List<IntrospectionDirective>? Directives { get; set; }
}

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

internal sealed class IntrospectionField
{
    public string Name { get; set; } = "";
    public List<IntrospectionInputValue>? Args { get; set; }
    public IntrospectionTypeRef Type { get; set; } = null!;
    public bool IsDeprecated { get; set; }
    public string? DeprecationReason { get; set; }
}

internal sealed class IntrospectionInputValue
{
    public string Name { get; set; } = "";
    public IntrospectionTypeRef Type { get; set; } = null!;
    public string? DefaultValue { get; set; }
}

internal sealed class IntrospectionEnumValue
{
    public string Name { get; set; } = "";
    public bool IsDeprecated { get; set; }
    public string? DeprecationReason { get; set; }
}

internal sealed class IntrospectionDirective
{
    public string Name { get; set; } = "";
    public List<string>? Locations { get; set; }
    public List<IntrospectionInputValue>? Args { get; set; }
}

internal sealed class IntrospectionTypeRef
{
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public IntrospectionTypeRef? OfType { get; set; }
}
