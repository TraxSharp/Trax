using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Loads the schema from a checked-in SDL file (typically <c>schema.graphql</c>). The file is
/// read once, parsed into an <see cref="ISchema"/>, and cached for the lifetime of the provider.
///
/// Use this when:
/// <list type="bullet">
/// <item>The live server endpoint isn't reachable at startup (CI, air-gapped environments).</item>
/// <item>You want startup validation against a known-good snapshot rather than whatever the
///       endpoint happens to return today.</item>
/// </list>
///
/// Keep the SDL file in sync with the server via a periodic introspection-snapshot job and
/// alert on drift separately. That makes "validation passes" and "schema is current" two
/// signals you can monitor independently.
/// </summary>
public class FileSchemaProvider : ISchemaProvider
{
    private readonly string _path;
    private readonly Lazy<Task<ISchema>> _schema;

    public FileSchemaProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _schema = new Lazy<Task<ISchema>>(
            LoadSchemaAsync,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.Value;

    private async Task<ISchema> LoadSchemaAsync()
    {
        if (!File.Exists(_path))
            throw new GraphQLSchemaIntrospectionException(
                $"SDL file '{_path}' not found. Expected an absolute or relative path to a "
                    + "GraphQL schema file (e.g. schema.graphql)."
            );

        string sdl;
        try
        {
            sdl = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Failed to read SDL file '{_path}'.",
                ex
            );
        }

        if (string.IsNullOrWhiteSpace(sdl))
            throw new GraphQLSchemaIntrospectionException(
                $"SDL file '{_path}' is empty. A valid schema must declare at least a Query root type."
            );

        try
        {
            return Schema.For(sdl);
        }
        catch (Exception ex)
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Failed to build schema from SDL file '{_path}'.",
                ex
            );
        }
    }
}
