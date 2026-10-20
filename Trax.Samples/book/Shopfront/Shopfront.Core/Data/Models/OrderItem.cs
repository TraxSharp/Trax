using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
