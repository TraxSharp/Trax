using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Shopfront.Data;

// The provider options the hub, the tests and the EF tools share.
public static class ShopfrontDatabase
{
    // The migrations' history goes in the shop's own schema, beside its tables.
    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shop");
}
