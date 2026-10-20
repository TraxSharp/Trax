namespace Shopfront.Data;

// Who is reading, as the shop's query filters see it.
public interface IShopfrontCaller
{
    string? CustomerId { get; }
    bool IsStaff { get; }
}
