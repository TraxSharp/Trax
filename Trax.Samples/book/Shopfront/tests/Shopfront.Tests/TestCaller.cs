using Shopfront.Data;

namespace Shopfront.Tests;

internal sealed record TestCaller(string? CustomerId, bool IsStaff) : IShopfrontCaller
{
    public static readonly TestCaller Staff = new(null, IsStaff: true);

    public static TestCaller Customer(string id) => new(id, IsStaff: false);
}
