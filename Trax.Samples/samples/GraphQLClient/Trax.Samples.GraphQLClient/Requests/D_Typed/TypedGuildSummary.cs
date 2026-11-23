using Trax.Api.GraphQL.Client.Typed;
using GraphQLType = Trax.Api.GraphQL.Client.Typed.GraphQLTypeAttribute;

namespace Trax.Samples.GraphQLClient.Requests.D_Typed;

[GraphQLType("Guild")]
public sealed record TypedGuildSummary(string Id, string Name);
