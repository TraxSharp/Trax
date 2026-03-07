---
layout: default
title: Migration (ChainSharp → Trax)
parent: Reference
nav_order: 3
---

# Migration from ChainSharp to Trax

## Target Framework

Trax targets `net10.0` exclusively. If your project is on `net8.0` or `net9.0`, you'll need to update your target framework before upgrading. ChainSharp 4.x is the last release supporting `net8.0`.

```xml
<!-- Before -->
<TargetFramework>net8.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

This affects your entire solution—every project that references a Trax package must target `net10.0`.

## Package Renames

Several packages were reorganized to reflect the layered architecture. The old package names no longer exist on NuGet:

| Old Package (ChainSharp) | New Package (Trax) |
|---|---|
| `Trax.Effect.Json` | `Trax.Effect.Provider.Json` |
| `Trax.Effect.Mediator` | `Trax.Mediator` |
| `Trax.Effect.Parameter` | `Trax.Effect.Provider.Parameter` |

## Namespace Changes

The `using` statements follow the new package names. Find-and-replace works for most of these:

| Old Namespace (ChainSharp) | New Namespace (Trax) |
|---|---|
| `Trax.Effect.Json` | `Trax.Effect.Provider.Json` |
| `Trax.Effect.Mediator` | `Trax.Mediator` |
| `Trax.Effect.Parameter` | `Trax.Effect.Provider.Parameter` |

## Dependency Updates

Trax aligns all dependencies with .NET 10. If your project pins any of these packages directly, update them:

| Package | ChainSharp Version | Trax Version |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | 8.0.x | 10.0.3+ |
| `Microsoft.EntityFrameworkCore.Relational` | 8.0.x | 10.0.3+ |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.x | 10.0.3+ |
| `Npgsql` | 8.0.x | 10.0.1+ |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.x | 10.0.0+ |
| `EFCore.NamingConventions` | 8.0.x | 10.0.1+ |
| `Microsoft.Extensions.*` | 8.0.x | 10.0.3+ |

These are transitive dependencies—Trax's NuGet packages pull in the correct versions automatically. You only need to act if your project has explicit `<PackageReference>` entries for any of these.

## Npgsql Enum Mapping (Breaking Change)

Npgsql 9.0+ changed how PostgreSQL enum types are registered with Entity Framework Core. If you have custom code that configures `NpgsqlDataSourceBuilder` or `UseNpgsql`, you need to add `MapEnum` calls inside the `UseNpgsql` options callback.

**Before (ChainSharp / Npgsql 8.x):**

Registering enums only on the `NpgsqlDataSourceBuilder` was sufficient:

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<MyEnum>();
var dataSource = dataSourceBuilder.Build();

services.AddDbContext<MyContext>(options =>
    options.UseNpgsql(dataSource));  // No MapEnum needed here
```

**After (Trax / Npgsql 10.x):**

You must also register enums in the `UseNpgsql` options callback:

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<MyEnum>();
var dataSource = dataSourceBuilder.Build();

services.AddDbContext<MyContext>(options =>
    options.UseNpgsql(dataSource, o =>
    {
        o.MapEnum<MyEnum>("my_enum");  // Now required
    }));
```

Both registrations are necessary—the `NpgsqlDataSourceBuilder` mapping handles the ADO.NET layer, and the `UseNpgsql` callback mapping handles the EF Core model layer. Omitting the latter causes `column "x" is of type my_enum but expression is of type integer` errors at runtime.

Trax.Core handles this internally for its own enum types (`TrainState`, `LogLevel`, `ScheduleType`, `DeadLetterStatus`). This only affects you if you've added custom PostgreSQL enum types to your own `DbContext` that extends `DataContext<T>`.

## Step-by-Step Upgrade

1. **Update your TFM** to `net10.0` across all projects in the solution.
2. **Replace package references.** Swap the old package names for the new ones in every `.csproj` file.
3. **Update dependencies.** If you pin EF Core, Npgsql, or `Microsoft.Extensions.*` packages directly, bump them to the versions listed above.
4. **Update enum mappings.** If you have custom PostgreSQL enum types, add `MapEnum` calls to your `UseNpgsql` callback (see above).
5. **Update `using` statements.** A global find-and-replace across `.cs` files covers it.
6. **Restore and build.** `dotnet restore && dotnet build` will surface anything you missed.

If you hit unresolved types after the rename, check the [Namespace Reference](scheduler/setup.md#namespace-reference) for the full namespace of scheduler-related types—some live in unexpected packages.

## ManifestGroup Migration (1.5+)

Version 1.5 promotes ManifestGroup from a denormalized string field (`group_id`) to a first-class database entity with per-group dispatch controls.

### Database Migration

Run `014_manifest_group.sql` against your database. This migration:

1. Creates the `trax.manifest_group` table with `name`, `max_active_jobs`, `priority`, `is_enabled`, and timestamp columns
2. Seeds ManifestGroup rows from existing `group_id` values on manifests
3. Adds a `manifest_group_id` foreign key (NOT NULL) on `trax.manifest`
4. Drops the old `group_id` column

The migration is idempotent — existing data is preserved and migrated automatically.

### Code Changes

- `Manifest.GroupId` (string?) is replaced by `Manifest.ManifestGroupId` (int) and `Manifest.ManifestGroup` (navigation property)
- `groupId` parameter is now available on `Schedule`, `ThenInclude`, `Include`, `ScheduleMany`, `ThenIncludeMany`, and `IncludeMany` (previously only on batch methods)
- When `groupId` is not specified, it defaults to the manifest's `externalId`
- Per-group `MaxActiveJobs`, `Priority`, and `IsEnabled` are configured from the dashboard (not from code)

### No Breaking Changes for Most Users

If you were not using `Manifest.GroupId` directly in your code, no source changes are needed beyond running the migration. The `groupId` parameter defaults are designed to be backward-compatible — each manifest gets its own group when no explicit groupId is provided.

## Scaling Indexes Migration (026)

Run `026_scaling_indexes.sql` against your database. This migration adds partial and composite indexes that prevent sequential scans at high row counts. All indexes use `CREATE INDEX IF NOT EXISTS` and are safe to re-run.

| Index | Table | Covers |
|-------|-------|--------|
| `ix_metadata_train_state_start_time` | `metadata` | Stale/stuck job queries (partial: `pending`, `in_progress` only) |
| `ix_metadata_start_time_desc` | `metadata` | Dashboard KPI aggregations, API pagination |
| `ix_metadata_manifest_id_train_state` | `metadata` | Active job counts per manifest group (partial: `pending`, `in_progress` only) |
| `ix_metadata_end_time_desc` | `metadata` | Health check failure counts (partial: non-null `end_time` only) |
| `ix_background_job_unfetched` | `background_job` | Worker dequeue query (partial: unfetched only) |
| `ix_work_queue_manifest_id_status_queued` | `work_queue` | Dormant dependent activation check (partial: `queued` only) |

These indexes are recommended for any deployment with more than a few thousand metadata rows. They have no effect on correctness — only on query performance.
