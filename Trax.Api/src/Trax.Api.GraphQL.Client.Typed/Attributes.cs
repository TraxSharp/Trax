namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Marks a request type as a POCO-derived GraphQL operation (mode D). The library walks the
/// type's properties at startup, consults the schema for the named result type, and generates
/// the query string from the POCO's shape. Use <see cref="OperationType"/> to disambiguate
/// queries from mutations - the kind cannot be inferred from the C# type alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLOperationAttribute : Attribute
{
    public GraphQLOperationAttribute(OperationType operationType)
    {
        OperationType = operationType;
    }

    public OperationType OperationType { get; }

    /// <summary>
    /// Optional explicit operation name. When omitted, the library uses the request type's
    /// name (stripping a trailing "Request" suffix).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional field name on the schema's root type. When omitted, the library uses the
    /// camel-cased operation name. Set when your POCO is called <c>GetPlayerRequest</c> but
    /// the schema field is <c>player</c>.
    /// </summary>
    public string? RootField { get; init; }
}

public enum OperationType
{
    Query,
    Mutation,
}

/// <summary>
/// Identifies the schema type that a result POCO represents. The generator uses this to look
/// up the corresponding object type in the schema and validate field presence + CLR-to-GraphQL
/// type compatibility for every property.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class GraphQLTypeAttribute : Attribute
{
    public GraphQLTypeAttribute(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TypeName = typeName;
    }

    public string TypeName { get; }
}

/// <summary>
/// Marks a property on the request type as a GraphQL operation variable. The property's value
/// is included in <c>Variables</c>; the generated query declares <c>$name: Type!</c> where
/// <c>name</c> is this attribute's value (or the camel-cased property name when omitted) and
/// <c>Type</c> is the matching field's input type on the root field.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLArgumentAttribute : Attribute
{
    /// <param name="graphQLType">
    /// The full GraphQL type annotation, e.g. <c>"String!"</c>, <c>"Int"</c>, or
    /// <c>"RenamePlayerInput!"</c>. Required because the CLR type alone is ambiguous: a
    /// C# <c>string</c> could be GraphQL <c>String</c>, <c>ID</c>, or a custom scalar.
    /// </param>
    public GraphQLArgumentAttribute(string graphQLType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphQLType);
        GraphQLType = graphQLType;
    }

    public string GraphQLType { get; }

    /// <summary>
    /// Optional override for the variable name in the generated query. Defaults to the
    /// camel-cased CLR property name.
    /// </summary>
    public string? VariableName { get; init; }
}

/// <summary>
/// Overrides the field selection name for a result-POCO property. By default the generator
/// uses the camel-cased property name. Apply this when the schema field name doesn't match
/// the C# convention.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLFieldAttribute : Attribute
{
    public GraphQLFieldAttribute(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        FieldName = fieldName;
    }

    public string FieldName { get; }
}
