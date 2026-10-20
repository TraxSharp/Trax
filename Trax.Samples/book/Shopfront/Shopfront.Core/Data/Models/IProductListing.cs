namespace Shopfront.Data.Models;

// What the catalogue shows anyone who asks.
public interface IProductListing
{
    string Sku { get; }
    string Name { get; }
    decimal Price { get; }
    int Stock { get; }
}
