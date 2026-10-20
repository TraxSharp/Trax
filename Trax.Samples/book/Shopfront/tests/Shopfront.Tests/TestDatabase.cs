using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Tests;

// The tests' own database, beside the application's, so a test never touches its rows.
internal static class TestDatabase
{
    public const string ConnectionString =
        "Host=localhost;Database=shopfront_tests;Username=postgres;Password=shopfront";

    // A context for a caller; staff, who read every order, unless the test says otherwise.
    public static ShopfrontDbContext NewContext(IShopfrontCaller? caller = null) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql(ConnectionString, ShopfrontDatabase.Npgsql)
                .Options,
            caller ?? TestCaller.Staff
        );

    // Drops the database and migrates a new one, as the hub does: the seeded catalogue, and no
    // orders or runs.
    public static async Task Reset()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public static async Task<int> StockOf(string sku)
    {
        await using var db = NewContext();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Stock).SingleAsync();
    }
}
