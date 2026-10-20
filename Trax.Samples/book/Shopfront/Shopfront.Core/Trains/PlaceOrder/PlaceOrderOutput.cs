namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(Guid OrderId, decimal Total, string TransactionId);
