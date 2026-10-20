namespace Shopfront.Trains.FulfilOrder;

public record FulfilOrderInput
{
    public required Guid OrderId { get; init; }
    public required int Items { get; init; }
}
