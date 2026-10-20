namespace Shopfront.Warehouse;

// Takes two seconds to choose a box and ten to pack, long enough to kill the worker while it does.
public class FakeWarehouse : IWarehouse
{
    public TimeSpan ChoosingTime { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PackingTime { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<Box> ChooseBox(int items, CancellationToken cancellationToken)
    {
        await Task.Delay(ChoosingTime, cancellationToken);
        return items <= 2 ? new Box("Small", 1m) : new Box("Large", 3m);
    }

    public Task Pack(
        Guid orderId,
        int items,
        string trackingNumber,
        CancellationToken cancellationToken
    ) => Task.Delay(PackingTime, cancellationToken);
}
