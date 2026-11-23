namespace Trax.Api.GraphQL.Client.Typed;

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
    /// C# <see langword="string"/> could be GraphQL <c>String</c>, <c>ID</c>, or a custom scalar.
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
