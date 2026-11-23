namespace Trax.Api.GraphQL.Client.Typed;

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
