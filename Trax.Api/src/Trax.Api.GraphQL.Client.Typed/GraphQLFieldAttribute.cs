namespace Trax.Api.GraphQL.Client.Typed;

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
