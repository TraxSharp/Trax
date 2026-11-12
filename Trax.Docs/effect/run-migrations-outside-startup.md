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

The host can do it itself. Start the image you are about to deploy once with `--trax-migrate`, before any process of
the new version serves, and start every serving process with `TRAX_MIGRATIONS=skip` in its environment instead of
calling `SkipMigrations()` in code, so one image does both:

```csharp compile
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Extensions;

var host = builder.Build();
await host.RunTraxAsync(args: []); // in place of host.Run()
```

```bash
dotnet Orders.dll --trax-migrate          # migrates, exits 0, serves nothing
TRAX_MIGRATIONS=skip dotnet Orders.dll    # serves, does not migrate
```

`RunTraxAsync` also applies the migrations of the application's own contexts named with `MigrateOnDeploy`. See
[TraxMigrate](/docs/sdk-reference/configuration/trax-migrate).

### Or from a program of your own


Without the host, run `DatabaseMigrator.Migrate` once per deployment, before any host of the new version starts
serving. A small console project referencing `Trax.Effect.Data.Postgres` is enough:

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

> [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
