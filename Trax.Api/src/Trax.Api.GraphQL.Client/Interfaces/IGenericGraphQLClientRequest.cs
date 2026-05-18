namespace Trax.Api.GraphQL.Client;

public interface IGenericGraphQLClientRequest
{
    string Query { get; }

    object? Variables => null;
}
