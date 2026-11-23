using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Samples.GraphQLClient.Gateway.Requests;

[GraphQLType("GetInvoiceOutput")]
public sealed record InvoiceView(string InvoiceId, int AmountCents, string Status);
