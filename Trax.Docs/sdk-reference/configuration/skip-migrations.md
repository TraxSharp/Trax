---
title: SkipMigrations
description: Reference for SkipMigrations, which turns off the schema migration UsePostgres and UseSqlite run during service registration.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 11
package: Trax.Effect.Data.Postgres
namespace: Trax.Effect.Data.Postgres.Extensions
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Extensions/ServiceExtensions.cs
---

# SkipMigrations

Turns off the schema migration that [UsePostgres](/docs/sdk-reference/configuration/use-postgres) and
[UseSqlite](/docs/sdk-reference/configuration/use-sqlite) run when they are called.

## Signature

```csharp fragment
public static TraxEffectBuilder SkipMigrations(
    this TraxEffectBuilder builder
)
```

## Returns

`TraxEffectBuilder`, the same builder, for chaining the data provider after it.

## Behavior

Sets `TraxEffectBuilder.MigrationsDisabled`. `UsePostgres` and `UseSqlite` read the flag when they are called, so
`SkipMigrations` comes before them in the chain; called after, it has no effect on that provider. Everything else the
provider registers is unchanged. The flag is carried over when the builder becomes `TraxEffectBuilderWithData`.

To skip without changing code, set `TRAX_MIGRATIONS=skip` in the environment instead, which a process started
with `--trax-migrate` overrides; a host whose code calls `SkipMigrations` refuses `--trax-migrate` (see
[TraxMigrate](/docs/sdk-reference/configuration/trax-migrate)).

With migrations skipped, the schema must already be current before the host serves work:
`DatabaseMigrator.Migrate(connectionString)` applies it from anywhere. See
[DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator).

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SkipMigrations()
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Run migrations outside startup](/docs/effect/run-migrations-outside-startup)
- [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate), [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator)
