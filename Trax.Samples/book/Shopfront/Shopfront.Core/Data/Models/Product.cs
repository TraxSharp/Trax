using System.ComponentModel.DataAnnotations;
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAllowAnonymous]
[TraxQueryModel(ExposeAs = typeof(IProductListing))]
public class Product : IProductListing
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }

    // Postgres's row version. A save that finds the row changed since it was read is refused.
    [Timestamp]
    public uint Version { get; set; }
}
