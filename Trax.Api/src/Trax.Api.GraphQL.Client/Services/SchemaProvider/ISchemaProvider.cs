using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

public interface ISchemaProvider
{
    Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default);
}
