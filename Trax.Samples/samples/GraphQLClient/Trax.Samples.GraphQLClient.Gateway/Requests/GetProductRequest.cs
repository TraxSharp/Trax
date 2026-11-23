using Trax.Api.GraphQL.Client.Typed;
using Trax.Samples.GraphQLClient.InventoryServer.Trains;

namespace Trax.Samples.GraphQLClient.Gateway.Requests;

// Typed requests for the two downstream servers. The Path names the schema namespace on the
// target server (discover.inventory / discover.billing); the DI key the Gateway resolves the
// executor with names the server itself. They are independent — see Program.cs.

[GraphQLOperation(OperationType.Query, Path = "discover.inventory", RootField = "getProduct")]
public sealed class GetProductRequest : TypedRequest<ProductView>
{
    [GraphQLArgument("GetProductInput!", VariableName = "input")]
    public required GetProductInput Input { get; init; }
}
