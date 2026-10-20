namespace Shopfront.Data;

// Nobody: a context built with no caller, by a tool or at startup, sees no orders.
public sealed class NoShopfrontCaller : IShopfrontCaller
{
    public static readonly NoShopfrontCaller Instance = new();

    public string? CustomerId => null;
    public bool IsStaff => false;
}
