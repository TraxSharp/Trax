using Trax.Effect.Attributes;

namespace Shopfront.Trains.PlaceOrder.Models;

internal record Payment(string TransactionId, decimal Amount, [TraxSensitive] string ReceiptUrl);
