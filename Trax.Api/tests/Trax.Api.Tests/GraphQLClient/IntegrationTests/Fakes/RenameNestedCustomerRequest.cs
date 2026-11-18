using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

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
