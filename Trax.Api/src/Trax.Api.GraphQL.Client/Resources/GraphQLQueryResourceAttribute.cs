namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Marks a request type as loading its <c>Query</c> from an embedded resource (mode E).
/// The <paramref name="resourceName"/> is resolved relative to the request type's namespace,
/// matching the default C# <c>EmbeddedResource</c> naming convention: a <c>GetPlayer.graphql</c>
/// file alongside a class in namespace <c>X.Y.Z</c> is the embedded resource
/// <c>X.Y.Z.GetPlayer.graphql</c>.
/// </summary>
/// <example>
/// <code>
/// [GraphQLQueryResource("GetPlayer.graphql")]
/// public sealed class GetPlayerRequest : GraphQLResourceRequest&lt;PlayerProfile&gt;
/// {
///     public required string Id { get; init; }
///     public override object? Variables => new { id = Id };
/// }
/// </code>
/// The consumer csproj must include the .graphql file as an embedded resource:
/// <code>
/// &lt;ItemGroup&gt;
///   &lt;EmbeddedResource Include="**/*.graphql" /&gt;
/// &lt;/ItemGroup&gt;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLQueryResourceAttribute : Attribute
{
    public GraphQLQueryResourceAttribute(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ResourceName = resourceName;
    }

    public string ResourceName { get; }
}
