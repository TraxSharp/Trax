// What the examples on cross-cutting/make-a-junction-cancellable.md take as given: the request
// and result types, the reader's own DbContext and the services the junctions call.
using Microsoft.EntityFrameworkCore;

public record CreateOrderRequest(string Sku, int Quantity);

public record OrderResult(string OrderId);

public record QuoteRequest(string Url);

public record Quote(decimal Price);

public record CustomerId(int Value);

public class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public interface IImageResizer
{
    byte[] Resize(byte[] image);
}

public record ImageBatch(IReadOnlyList<byte[]> Images);

public record ResizedImages(IReadOnlyList<byte[]> Images);

public record Invoice(string Number);

public record InvoiceReceipt(string Number, DateTime SentAt);

public interface IInvoiceSender
{
    Task<InvoiceReceipt> SendAsync(Invoice invoice, CancellationToken ct);
}
