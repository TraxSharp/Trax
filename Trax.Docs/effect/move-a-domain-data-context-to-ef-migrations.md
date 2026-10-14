---
title: Move a domain data context to EF migrations
description: "Replace EnsureSchemaCreatedAsync with EF Core migrations for a DomainDataContext, including a database that EnsureSchemaCreatedAsync already created."
type: how-to
area: data
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext
---

# Move a domain data context to EF migrations

A `DomainDataContext` takes ordinary EF Core migrations. Trax's own tables are migrated separately, by `UsePostgres` during service registration and tracked in `trax.migrations`, so the two never touch each other's history. This page assumes a context set up as in [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts).

## 1. Add the EF tools

The design package goes in the project that holds the context, and the `dotnet-ef` tool at the same EF Core version as the rest of the app:

```bash
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

## 2. Keep the provider options in one place

The app and the tools must agree on them. Put the migrations history table in the domain's own schema; without this it lands in `public."__EFMigrationsHistory"`, shared by every context that does the same:

```csharp compile=shop
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

public static class ShopDatabase
{
    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shop");
}
```

## 3. Add a design-time factory

`dotnet ef` builds the context from it instead of starting your host, which would run Trax's migrations and the rest of `Program.cs`. The base's sealed `OnModelCreating` is no obstacle: the tools build the model through it, schema and UTC converter included, so the migrations carry `EnsureSchema("shop")` and every table in that schema.

```csharp compile=shop
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class ShopDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql("Host=localhost;Database=shop;Username=app;Password=app", ShopDatabase.Npgsql)
            .Options);
}
```

`dotnet ef migrations add` builds the model offline and does not connect to that database.

## 4. Apply migrations at startup

Apply them at startup, or from a deployment step, in place of `EnsureSchemaCreatedAsync`:

```csharp compile=shop
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;

builder.Services.AddDomainDataContext<IShopDbContext, ShopDbContext>(o =>
    o.UseNpgsql(connectionString, ShopDatabase.Npgsql));
builder.Services.AddTrax(trax => trax.AddEffects(e => e.UsePostgres(connectionString)));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShopDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
```

## 5. Generate the first migration

```bash
dotnet ef migrations add Initial -o Data/Migrations
```

On a fresh database the first `MigrateAsync` logs one `Error` from EF Core, a failed `SELECT ... FROM shop."__EFMigrationsHistory"` before it creates that table; later starts log none. `MigrateAsync` refuses to start the host (`PendingModelChangesWarning`) when the model has changed without a migration. The `NoPendingModelChanges` [architecture guard](/docs/reference/architecture-guards) catches that in CI: list the context in `MigrationContexts`.

## A database EnsureSchemaCreatedAsync already created

It has the tables and no history, so the first `MigrateAsync` fails with `42P07: relation "Orders" already exists`. Generate `Initial` from the model exactly as deployed, then record it as applied once, before the new code starts:

```sql
CREATE TABLE IF NOT EXISTS shop."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
    "ProductVersion" character varying(32) NOT NULL);
INSERT INTO shop."__EFMigrationsHistory" VALUES ('20261003182101_Initial', '10.0.12');
```

Use the migration's full id (its file name) and your EF Core version. Every later migration then applies normally.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [Architecture guards](/docs/reference/architecture-guards)
