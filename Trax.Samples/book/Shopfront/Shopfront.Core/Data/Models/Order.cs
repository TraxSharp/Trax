using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
[TraxQueryModel]
public class Order
{
    public Guid Id { get; set; }
    public required string CustomerId { get; set; }
    public decimal Total { get; set; }
    public required string TransactionId { get; set; }
    public DateTime PlacedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
