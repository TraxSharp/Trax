// What the example on sdk-reference/trains-and-junctions/train-exception.md names but does not
// declare: the order and the shipping service.
public record Order(long Id, IReadOnlyList<string> Lines);

public record Shipment(string TrackingNumber);

public interface IShipping
{
    Task<Shipment> Book(Order order, CancellationToken ct);
}
