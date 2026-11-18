namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Marks a <see cref="TypedRequest{TResponse}"/> as a POCO-derived GraphQL operation. On first
/// access of <c>Query</c> the library generates the document from the request's
/// <see cref="GraphQLArgumentAttribute"/> properties and the result type's properties; the
/// executor then validates it against the schema like any other request. Use
/// <see cref="OperationType"/> to say whether it is a query or a mutation, since that cannot be
/// inferred from the C# type.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-client/typed-request">SDK reference</seealso>
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
