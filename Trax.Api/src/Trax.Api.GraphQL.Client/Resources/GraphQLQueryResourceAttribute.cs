namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Marks a request type as loading its <c>Query</c> from an embedded resource (mode E).
/// The resource name is resolved relative to the request type's namespace,
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
    /// <summary>Names the embedded resource that holds the request's query.</summary>
    /// <param name="resourceName">The resource file name relative to the request type's namespace, for example <c>GetPlayer.graphql</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="resourceName"/> is null, empty or whitespace.</exception>
    public GraphQLQueryResourceAttribute(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ResourceName = resourceName;
    }

    /// <summary>
    /// The resource file name. Looked up as <c>{namespace}.{name}</c>, then
    /// <c>{assembly}.{name}</c>, then as given, with <c>/</c> and <c>\</c> read as dots. When none of
    /// those exists, the one resource whose name ends with <c>.{name}</c> (ignoring case) is used, and
    /// a name more than one resource ends with is refused: qualify it with its folder.
    /// </summary>
    public string ResourceName { get; }
}
