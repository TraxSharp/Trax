using Trax.Api.GraphQL.Client.Typed;
using GraphQLType = Trax.Api.GraphQL.Client.Typed.GraphQLTypeAttribute;

namespace Trax.Samples.GraphQLClient.Requests.D_Typed;

[GraphQLType("Item")]
public sealed record TypedItemSummary(string Id, string Name, string Category);
