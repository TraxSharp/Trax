---
title: Run migrations outside startup
description: How to stop a Trax host migrating the database when it starts, and apply the schema from a deployment step instead, for Lambda, serverless and replica hosts.
type: how-to
area: data
nav_order: 20
---

# Run migrations outside startup

By default `UsePostgres` and `UseSqlite` migrate the schema while the host registers its services. Move that out of
the host when:

- it is a Lambda runner or another serverless function, where every cold start would open a connection and take the
  migration lock even when nothing is pending;
- it connects to a read replica, which must never be migrated;
- a deployment step already migrates the database before the new version starts.

## 1. Skip the migration in the host

Call `SkipMigrations()` before the data provider:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SkipMigrations()
        .UsePostgres(builder.Configuration.GetConnectionString("Trax")!)
    )
);

builder.Build().Run();
```

Called after `UsePostgres`, it changes nothing: the migration has already run.

## 2. Migrate from the deployment

Run `DatabaseMigrator.Migrate` once per deployment, before any host of the new version starts serving. A small
console project referencing `Trax.Effect.Data.Postgres` is enough:

```csharp compile
using Trax.Effect.Data.Postgres.Utils;

var connectionString =
    Environment.GetEnvironmentVariable("TRAX_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set TRAX_CONNECTION_STRING.");

await DatabaseMigrator.Migrate(connectionString);
```

It is safe to run from several places at once: the migrator holds an advisory lock for the whole run, so a second
caller waits and then finds nothing pending. For SQLite, `Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator.Migrate` does
the same for a database file.

## 3. Keep one host migrating, if you want

Hosts that do not skip still migrate on startup. A common split is an API or scheduler host that migrates and Lambda
runners that skip, so the schema moves forward when the long-running host deploys.

## See also

- [Database migrations](/docs/reference/schema-migrations), what each migration changes

## SDK Reference

> [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) | [DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
