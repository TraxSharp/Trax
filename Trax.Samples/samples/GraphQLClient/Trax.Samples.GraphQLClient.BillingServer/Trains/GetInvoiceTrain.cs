using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.BillingServer.Trains;

public record GetInvoiceInput(string InvoiceId);

public record GetInvoiceOutput(string InvoiceId, int AmountCents, string Status);

public interface IGetInvoiceTrain : IServiceTrain<GetInvoiceInput, GetInvoiceOutput>;

// Exposed at discover.billing.getInvoice. A genuinely different schema from the inventory
// server: different types, different fields. A client keyed to the inventory server cannot
// run this query (its schema has no getInvoice), which is the isolation the keyed client
// guarantees.
[TraxAllowAnonymous]
[TraxQuery(Namespace = "billing", Description = "Looks up an invoice by id.")]
public class GetInvoiceTrain : ServiceTrain<GetInvoiceInput, GetInvoiceOutput>, IGetInvoiceTrain
{
    protected override Task<Either<Exception, GetInvoiceOutput>> Junctions() =>
        Chain<GetInvoiceJunction>().Resolve();
}

internal sealed class GetInvoiceJunction : Junction<GetInvoiceInput, GetInvoiceOutput>
{
    private static readonly IReadOnlyDictionary<string, GetInvoiceOutput> Invoices = new Dictionary<
        string,
        GetInvoiceOutput
    >
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
