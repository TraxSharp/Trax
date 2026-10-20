namespace Shopfront.Warehouse;

public interface IWarehouse
{
    // The box an order of this many items goes in, and what it weighs packed.
    Task<Box> ChooseBox(int items, CancellationToken cancellationToken);

    // Packs the order's items and puts the label on the parcel.
    Task Pack(Guid orderId, int items, string trackingNumber, CancellationToken cancellationToken);
}
