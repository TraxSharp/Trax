using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Fakes targeting the nested-path branch of <see cref="TypedQueryGenerator"/> and
/// <see cref="TypedRequest{T}"/>. The Trax server groups fields under
/// <c>discover.{namespace}</c>; <see cref="GraphQLOperationAttribute.Path"/> teaches the
/// generator and the default extractor to walk that envelope without a custom Extract
/// override.
/// </summary>
[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "typedCustomer")]
public sealed class GetNestedCustomerByEmailRequest : TypedRequest<TypedPlayerProfile?>
{
    [GraphQLArgument("String!", VariableName = "email")]
    public required string Email { get; init; }
}

[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "typedCustomers")]
public sealed class GetNestedCustomersRequest : TypedRequest<IReadOnlyList<TypedItemSummary>>
{
    // No arguments — exercises the wrapper emission with an argumentless inner field.
}

[GraphQLOperation(OperationType.Query, Path = "discover.players", RootField = "typedPlayerByRank")]
public sealed class GetNestedPlayerByRankRequest : TypedRequest<TypedPlayerProfile?>
{
    [GraphQLArgument("Rank!", VariableName = "rank")]
    public required string Rank { get; init; }
}

[GraphQLOperation(OperationType.Query, Path = "discover", RootField = "netsuite")]
public sealed class SingleLevelPathRequest : TypedRequest<NetsuiteNamespaceShape>
{
    // Single-segment path. The inner root field projects the namespace object itself.
}

[GraphQLType("NetsuiteQueries")]
public sealed record NetsuiteNamespaceShape(IReadOnlyList<TypedItemSummary> TypedCustomers);

/// <summary>
/// Mutation flavour of the nested-envelope request. Trax exposes mutations under
/// <c>dispatch.{namespace}</c> the same way it exposes queries under
/// <c>discover.{namespace}</c>; the generator and the extractor must work identically for
/// both <see cref="OperationType.Mutation"/> and <see cref="OperationType.Query"/>.
/// </summary>
[GraphQLOperation(OperationType.Mutation, Path = "dispatch.netsuite", RootField = "renameCustomer")]
public sealed class RenameNestedCustomerRequest : TypedRequest<TypedPlayerProfile>
{
    [GraphQLArgument("RenamePlayerInput!", VariableName = "input")]
    public required RenamePlayerInput Input { get; init; }
}
