using Microsoft.EntityFrameworkCore;
using Shopfront.Suppliers;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            // Another order changed the stock first; trying again reads the new stock.
            DbUpdateConcurrencyException => FailureClass.Transient,
            // The supplier's feed is down; it comes back.
            SupplierUnavailableException => FailureClass.Transient,
            _ => null,
        };
}
