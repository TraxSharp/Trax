using Trax.Api.GraphQL.Client.Typed;
using Trax.Samples.GraphQLClient.BillingServer.Trains;
using Trax.Samples.GraphQLClient.InventoryServer.Trains;

namespace Trax.Samples.GraphQLClient.Gateway.Requests;

// Typed requests for the two downstream servers. The Path names the schema namespace on the
// target server (discover.inventory / discover.billing); the DI key the Gateway resolves the
// executor with names the server itself. They are independent — see Program.cs.

[GraphQLType("GetProductOutput")]
public sealed record ProductView(string Sku, string Name, int QuantityOnHand);

[GraphQLOperation(OperationType.Query, Path = "discover.inventory", RootField = "getProduct")]
public sealed class GetProductRequest : TypedRequest<ProductView>
{
    [GraphQLArgument("GetProductInput!", VariableName = "input")]
    public required GetProductInput Input { get; init; }
}

[GraphQLType("GetInvoiceOutput")]
public sealed record InvoiceView(string InvoiceId, int AmountCents, string Status);

[GraphQLOperation(OperationType.Query, Path = "discover.billing", RootField = "getInvoice")]
public sealed class GetInvoiceRequest : TypedRequest<InvoiceView>
{
    [GraphQLArgument("GetInvoiceInput!", VariableName = "input")]
    public required GetInvoiceInput Input { get; init; }
}
