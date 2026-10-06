using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Loads the schema from a checked-in SDL file (typically <c>schema.graphql</c>). The file is
/// read once, parsed into an <see cref="ISchema"/>, and cached for the lifetime of the provider.
/// Custom scalars the SDL declares (<c>Any</c>, <c>UUID</c>, <c>URL</c>, ...) are accepted as
/// permissive scalars, as <see cref="IntrospectingSchemaProvider"/> accepts them.
/// A read that fails (the file is missing, empty or not valid SDL) is not cached: the file is read
/// again once a capped, jittered backoff has passed.
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
    private readonly bool _removeSubscriptions;
    private readonly RetryingAsyncLazy<ISchema> _schema;

    /// <summary>
    /// Creates a provider for the SDL file at <paramref name="path"/> that drops the subscription
    /// type, the client's default. The file is not read until the schema is first requested.
    /// </summary>
    /// <param name="path">An absolute path, or one relative to the process's working directory.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    public FileSchemaProvider(string path)
        : this(path, removeSubscriptionsFromSchema: true) { }

    /// <summary>Creates a provider for the SDL file at <paramref name="path"/>. The file is not read until the schema is first requested.</summary>
    /// <param name="path">An absolute path, or one relative to the process's working directory.</param>
    /// <param name="removeSubscriptionsFromSchema">
    /// Whether the subscription type is dropped before queries are validated, as
    /// <see cref="IGraphQLClientConfiguration.RemoveSubscriptionsFromSchema"/> says.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    public FileSchemaProvider(string path, bool removeSubscriptionsFromSchema)
        : this(path, removeSubscriptionsFromSchema, TimeProvider.System) { }

    internal FileSchemaProvider(string path, bool removeSubscriptionsFromSchema, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _removeSubscriptions = removeSubscriptionsFromSchema;
        _schema = new RetryingAsyncLazy<ISchema>(LoadSchemaAsync, time);
    }

    /// <summary>
    /// Returns the schema, loading it on the first call and sharing the result. A load that
    /// fails is not kept: it is loaded again once a capped, jittered backoff has passed, and a call
    /// before then gets the failure. <paramref name="cancellationToken"/>
    /// cancels this caller's wait, not a load other callers share.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the schema.</param>
    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.GetValueAsync(cancellationToken);

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
            return SdlSchema.Build(sdl, _removeSubscriptions);
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
