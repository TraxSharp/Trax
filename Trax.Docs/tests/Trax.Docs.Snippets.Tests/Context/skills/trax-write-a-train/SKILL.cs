// What the examples in skills/trax-write-a-train/SKILL.md take as given: the app's own services
// the junctions are built with.
public interface ICatalog
{
    Task<decimal> PriceOf(string sku, CancellationToken cancellationToken);
}

public interface IOrderStore
{
    Task<long> Save(string sku, int quantity, decimal total, CancellationToken cancellationToken);
}
