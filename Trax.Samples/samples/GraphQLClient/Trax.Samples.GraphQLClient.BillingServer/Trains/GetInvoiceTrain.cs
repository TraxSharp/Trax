using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.BillingServer.Trains;

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
