namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Marks a <see cref="TypedRequest{TResponse}"/> as a POCO-derived GraphQL operation. On first
/// access of <c>Query</c> the library generates the document from the request's
/// <see cref="GraphQLArgumentAttribute"/> properties and the result type's properties; the
/// executor then validates it against the schema like any other request. Use
/// <see cref="OperationType"/> to say whether it is a query or a mutation, since that cannot be
/// inferred from the C# type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLOperationAttribute : Attribute
{
    /// <summary>Declares the request as a query or a mutation.</summary>
    /// <param name="operationType">Which operation keyword the generated document uses.</param>
    public GraphQLOperationAttribute(OperationType operationType)
    {
        OperationType = operationType;
    }

    /// <summary>Whether the generated document is a query or a mutation.</summary>
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

    /// <summary>
    /// Optional dot-separated path of wrapper field names that nest above <see cref="RootField"/>.
    /// Set to <c>"discover.netsuite"</c> when the schema groups fields under
    /// <c>query { discover { netsuite { rootField { ... } } } }</c>. This matches the envelope
    /// the Trax server produces for trains decorated with <c>[TraxQuery(Namespace = "netsuite")]</c>.
    /// When omitted, the root field is emitted directly under the Query type.
    /// </summary>
    public string? Path { get; init; }
}

/// <summary>The kind of operation a typed request generates. Subscriptions are not supported.</summary>
public enum OperationType
{
    /// <summary>A <c>query</c> operation.</summary>
    Query,

    /// <summary>A <c>mutation</c> operation.</summary>
    Mutation,
}

/// <summary>
/// Identifies the schema type that a result POCO represents. A typed request's result type (or
/// its element type, for a list) must carry it, and a nested property whose type carries it is
/// selected with its own sub-selection. The generator does not read <see cref="TypeName"/> or
/// consult the schema; field presence is checked when the generated query is validated.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class GraphQLTypeAttribute : Attribute
{
    /// <summary>Marks a class as a result type representing the named schema type.</summary>
    /// <param name="typeName">The schema type's name, for example <c>Player</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="typeName"/> is null, empty or whitespace.</exception>
    public GraphQLTypeAttribute(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TypeName = typeName;
    }

    /// <summary>The schema type's name, as passed to the constructor.</summary>
    public string TypeName { get; }
}

/// <summary>
/// Marks a property on the request type as a GraphQL operation variable. The property's value
/// is included in <c>Variables</c>; the generated query declares <c>$name: Type</c> where
/// <c>name</c> is <see cref="VariableName"/> (or the camel-cased property name when omitted) and
/// <c>Type</c> is <see cref="GraphQLType"/>, and passes it to the root field as the argument of
/// the same name.
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

    /// <summary>The GraphQL type the variable is declared with, for example <c>String!</c>.</summary>
    public string GraphQLType { get; }

    /// <summary>
    /// Optional override for the variable name in the generated query. Defaults to the
    /// camel-cased CLR property name.
    /// </summary>
    public string? VariableName { get; init; }
}

/// <summary>
/// Names the schema field a result-POCO property is read from. Without it the generator selects
/// the property's <c>[JsonPropertyName]</c>, or else the camel-cased property name. When the field
/// differs from that response key, the generator aliases it (<c>displayName: name</c>), so the
/// server answers under the key the property is deserialized from and no matching
/// <c>[JsonPropertyName]</c> is needed. A <c>[JsonPropertyName]</c> that is not a valid GraphQL
/// name cannot be an alias, and the query is refused when generated.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLFieldAttribute : Attribute
{
    /// <summary>Selects <paramref name="fieldName"/> for this property.</summary>
    /// <param name="fieldName">The schema field name.</param>
    /// <exception cref="ArgumentException"><paramref name="fieldName"/> is null, empty or whitespace.</exception>
    public GraphQLFieldAttribute(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        FieldName = fieldName;
    }

    /// <summary>The schema field name selected for the property.</summary>
    public string FieldName { get; }
}
