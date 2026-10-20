using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopfront.Data;

// What dotnet ef builds the context from, instead of starting the hub. It reads the model and
// connects to nothing.
public class ShopfrontDbContextFactory : IDesignTimeDbContextFactory<ShopfrontDbContext>
{
    public ShopfrontDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=shopfront", ShopfrontDatabase.Npgsql)
                .Options
        );
}
