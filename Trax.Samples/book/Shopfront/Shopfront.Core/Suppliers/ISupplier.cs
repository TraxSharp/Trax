namespace Shopfront.Suppliers;

public interface ISupplier
{
    // Delivers what the supplier can of each SKU: a list of (SKU, units) actually sent.
    Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    );
}
