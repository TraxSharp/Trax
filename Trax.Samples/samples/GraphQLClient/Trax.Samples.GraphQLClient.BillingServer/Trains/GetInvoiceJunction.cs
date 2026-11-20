using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.BillingServer.Trains;

internal sealed class GetInvoiceJunction : Junction<GetInvoiceInput, GetInvoiceOutput>
{
    private static readonly IReadOnlyDictionary<string, GetInvoiceOutput> Invoices = new Dictionary<
        string,
        GetInvoiceOutput
    >(StringComparer.Ordinal)
    {
        ["INV-1"] = new("INV-1", 4_999, "Paid"),
        ["INV-2"] = new("INV-2", 12_500, "Pending"),
    };

    public override Task<GetInvoiceOutput> Run(GetInvoiceInput input) =>
        Task.FromResult(
            Invoices.TryGetValue(input.InvoiceId, out var invoice)
                ? invoice
                : new GetInvoiceOutput(input.InvoiceId, 0, "Unknown")
        );
}
