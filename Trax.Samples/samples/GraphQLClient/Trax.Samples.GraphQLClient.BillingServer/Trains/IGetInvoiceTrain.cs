using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.BillingServer.Trains;

public interface IGetInvoiceTrain : IServiceTrain<GetInvoiceInput, GetInvoiceOutput>;
