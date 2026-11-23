using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Samples.GraphQLClient.Gateway.Requests;

[GraphQLType("GetProductOutput")]
public sealed record ProductView(string Sku, string Name, int QuantityOnHand);
