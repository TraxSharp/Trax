using Trax.Api.GraphQL.Client.Typed;
using Trax.Samples.GraphQLClient.BillingServer.Trains;

namespace Trax.Samples.GraphQLClient.Gateway.Requests;

[GraphQLOperation(OperationType.Query, Path = "discover.billing", RootField = "getInvoice")]
public sealed class GetInvoiceRequest : TypedRequest<InvoiceView>
{
    [GraphQLArgument("GetInvoiceInput!", VariableName = "input")]
    public required GetInvoiceInput Input { get; init; }
}
