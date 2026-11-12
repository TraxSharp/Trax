---
title: TraxMigrate
description: Reference for --trax-migrate, TRAX_MIGRATIONS, MigrateOnDeploy and RunTraxAsync, which let one image both migrate in a deploy step and serve.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 16
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Extensions
source:
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/TraxMigrate.cs
  - Trax.Effect/src/Trax.Effect.Data/Extensions/TraxDeployExtensions.cs
---

# TraxMigrate

Decides whether a host migrates the database while its services are registered, and lets a deployment start the
same image once with `--trax-migrate` to migrate and exit before the new version serves.

## Signatures

```csharp fragment
public static TraxEffectBuilderWithData MigrateOnDeploy<TContext>(
    this TraxEffectBuilderWithData builder
) where TContext : DbContext;

public static Task RunTraxAsync(
    this IHost host,
    string[]? args = null,
    CancellationToken cancellationToken = default
);
```

`TraxMigrate`, in `Trax.Effect.Configuration.Deploy` (package `Trax.Effect`), holds the argument and the variable:
`Argument`, `Variable`, `SkipValue`, `Requested` and `RequestedIn(args)`.

## Members

| Member | What it is |
|---|---|
| `Argument` | `--trax-migrate`. A process started with it migrates and exits through `RunTraxAsync`. |
| `Variable`, `SkipValue` | `TRAX_MIGRATIONS=skip` in the environment stops `UsePostgres` and `UseSqlite` migrating while they register. Any other non-empty value refuses to start. |
| `Requested` | Whether this process's command line contains `--trax-migrate`. |
| `RequestedIn(args)` | Whether `args` contain `--trax-migrate`. |
| `MigrateOnDeploy<TContext>()` | Names one of the application's own contexts for a migrate process to bring up to date. |
| `RunTraxAsync(host, args)` | Runs the host as `host.RunAsync()` does, or, for a migrate process, migrates the named contexts and returns without starting it. |

## Behavior

`UsePostgres` and `UseSqlite` decide whether to migrate when they are called:

| The process | Migrates while registering |
|---|---|
| started with `--trax-migrate` | yes, even with `TRAX_MIGRATIONS=skip` |
| started with `--trax-migrate`, code calls `SkipMigrations()` | refuses to start: the code says this host never migrates |
| `TRAX_MIGRATIONS=skip` | no |
| code calls `SkipMigrations()` | no |
| otherwise | yes |

Both are read from the process rather than from configuration, because the migration runs while services are
registered, before configuration can be resolved. A process exporting its client manifest
(`--trax-export-client`) never migrates.

`RunTraxAsync` on a migrate process applies the pending EF migrations of each context named with
`MigrateOnDeploy`, in the order they were named. A context with no EF migrations has its missing tables created
instead, as [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context)'s `EnsureSchemaCreatedAsync`
does. It logs what each did and returns without starting the host, so no hosted service runs and nothing listens.
A context named without an `IDbContextFactory<TContext>` registered throws, naming the registration to add.

A host started with `--trax-migrate` that calls `app.Run()` rather than `RunTraxAsync` refuses to start, naming
`RunTraxAsync`, so a migrate process never also starts the host's workers and listeners. On a `WebApplication`,
`RunTraxAsync` from `Trax.Api.GraphQL.PersistedOperations.Extensions` handles `--trax-migrate` the same way, as
well as `--trax-export-client` (see [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest)).

## Example

```csharp compile
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("App"))
);
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(builder.Configuration.GetConnectionString("Trax")!)
        .MigrateOnDeploy<AppDbContext>()
    )
);

var host = builder.Build();
await host.RunTraxAsync(args: []);
```

A deployment runs it once with `--trax-migrate`, and every serving process with `TRAX_MIGRATIONS=skip`.

## See also

- [Run migrations outside startup](/docs/effect/run-migrations-outside-startup)
- [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations), [DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator)
