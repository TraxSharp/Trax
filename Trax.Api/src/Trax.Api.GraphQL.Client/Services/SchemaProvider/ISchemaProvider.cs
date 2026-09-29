using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Supplies the server schema that outbound queries are validated against. Built in:
/// <see cref="IntrospectingSchemaProvider"/> (the default), <see cref="FileSchemaProvider"/>, and,
/// in <c>Trax.Api.GraphQL.Client.Trax</c>, an in-process provider built from the server's own
/// schema configuration.
/// </summary>
public interface ISchemaProvider
{
    /// <summary>
    /// Returns the schema. The built-in providers load it once, on the first call, and return that
    /// result (or that failure) on every later call; they ignore <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load, for providers that support it.</param>
    /// <exception cref="GraphQLSchemaIntrospectionException">The schema could not be loaded.</exception>
    Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default);
}
