namespace Shopfront.Trains.ReturnOrder;

// What a return's Checking state hands the train: the order from the return's context, and the
// return's owner, whom the server names.
public record ReturnOrderInput
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
}
