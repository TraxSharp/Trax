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
    /// Returns the schema. The built-in providers load it on the first call and share that schema
    /// with every later call. A load that fails is not kept: the next call loads again, so a
    /// server that was unreachable once does not leave the client unable to validate.
    /// <paramref name="cancellationToken"/> cancels the caller's wait; a load other callers are
    /// waiting on carries on.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the schema.</param>
    /// <exception cref="GraphQLSchemaIntrospectionException">The schema could not be loaded.</exception>
    Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default);
}
