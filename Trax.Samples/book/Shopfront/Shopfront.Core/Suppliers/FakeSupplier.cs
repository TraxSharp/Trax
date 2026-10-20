namespace Shopfront.Suppliers;

// Sends a carton of five of anything asked for, unless it has been taken down.
public class FakeSupplier : ISupplier
{
    public const int Carton = 5;

    public bool IsDown { get; set; }

    public Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    )
    {
        if (IsDown)
            throw new SupplierUnavailableException("The supplier's feed did not answer.");
        return Task.FromResult<IReadOnlyList<(string Sku, int Units)>>(
            [.. skus.Select(sku => (sku, Carton))]
        );
    }
}
