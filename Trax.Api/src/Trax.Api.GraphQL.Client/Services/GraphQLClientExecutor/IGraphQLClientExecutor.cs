namespace Trax.Api.GraphQL.Client;

public interface IGraphQLClientExecutor
{
    Task<TReturn> Run<TReturn>(
        IGraphQLClientRequest<TReturn> request,
        CancellationToken cancellationToken = default
    );
}
