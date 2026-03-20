---
layout: default
title: SkipMigrations
parent: Configuration
grand_parent: SDK Reference
nav_order: 2
---

# SkipMigrations

Disables the automatic database migration that normally runs inside [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect). When called before `UsePostgres()`, the schema migration step is skipped entirely.

## Signature

```csharp
public static TraxEffectBuilder SkipMigrations(
    this TraxEffectBuilder builder
)
```

## Returns

`TraxEffectBuilder` -- the same builder instance for chaining. Call `UsePostgres()` after this method.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SkipMigrations()
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
```

## When to Use

Use `SkipMigrations()` in environments where database migrations are managed externally:

- **AWS Lambda runners** -- cold start time is critical; migrations add 500ms--3s+ of blocking I/O on every cold start, even when no migrations are pending
- **Serverless functions** (Google Cloud Run, Azure Functions) -- same cold start concern
- **Read-only replicas** -- the function connects to a read replica that should never be migrated
- **CI/CD-managed migrations** -- migrations run as a separate deployment step before application startup

## Running Migrations Separately

`DatabaseMigrator.Migrate()` is a public static method that can be called from any context:

```csharp
using Trax.Effect.Data.Postgres.Utils;

// In a CI/CD script, setup Lambda, or API startup:
await DatabaseMigrator.Migrate(connectionString);
```

In the default setup (without `SkipMigrations()`), this is called automatically inside `UsePostgres()`. When you call `SkipMigrations()`, you are responsible for ensuring migrations run elsewhere before the application handles requests.

## Remarks

- Must be called **before** `UsePostgres()` in the fluent chain. `UsePostgres()` checks the `MigrationsDisabled` flag at call time.
- All other registrations (`NpgsqlDataSource`, `IDbContextFactory`, `IDataContext`, effects) are unaffected -- only the migration step is skipped.
- The flag survives builder promotion to `TraxEffectBuilderWithData`, so it works correctly with method chaining.

## Package

```
dotnet add package Trax.Effect.Data.Postgres
```
