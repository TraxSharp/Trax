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
