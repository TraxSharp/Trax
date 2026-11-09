---
title: Switch a template to Postgres
description: "Move a project made from a Trax template off the in-memory provider onto Postgres, so runs survive a restart and queued work runs."
type: how-to
area: tooling
nav_order: 4
source:
  - Trax.Samples/templates/content
---

# Switch a template to Postgres

The [project templates](/docs/reference/templates) record runs with the in-memory provider, which
loses them on restart, shares nothing between processes, and leaves `mode: QUEUE` refused with
`TRAX_QUEUE_UNAVAILABLE`. These steps move Trax's own tables to Postgres.

1. Start a Postgres and add the provider package, at the version the project's other `Trax.*`
   packages already have in `Directory.Packages.props`:

   ```bash
   docker run -d --name trax-db -p 127.0.0.1:5432:5432 \
     -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
   dotnet add package Trax.Effect.Data.Postgres --version <the Trax.Effect version>
   ```

2. Put the connection string in `appsettings.Development.json`:

   ```json
   {
     "ConnectionStrings": {
       "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
     }
   }
   ```

3. In `Program.cs`, add `using Trax.Effect.Data.Postgres.Extensions;` and replace `UseInMemory()`
   in `AddEffects` with:

   ```csharp fragment
   effects.UsePostgres(
       builder.Configuration.GetConnectionString("TraxDatabase")
           ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
   )
   ```

4. Run the host. Trax creates and migrates its `trax` schema at startup, and the dashboard's runs
   now survive a restart.

With a database, the scheduler queues work in it and runs it on local workers, so a
`[TraxMutation]` called with `mode: QUEUE` runs, and several processes on one database share the
work. `HelloWorldTrain` exposes `Run` only; change its `[TraxMutation(GraphQLOperation.Run)]` to
`[TraxMutation]` to serve the queue mode too.

The schema changes each release applies are listed in
[Schema migrations](/docs/reference/schema-migrations).

## Move the application context

In the `trax-hub` and `trax-api` templates the application context, `AppDbContext`, is separate
and stays in memory. Its tables go under the schema in `Data/AppSchema.cs`, and on Postgres they
need EF Core migrations, as the comment above `EnsureCreated()` in `Program.cs` says.
`EnsureCreated()` creates nothing in a database that already has tables, and `UsePostgres` has
created Trax's by the time it runs, so the host starts without error and the first query of
`AppDbContext` fails with `42P01: relation "app.notes" does not exist`.

1. Add the EF Core packages and the `dotnet ef` tool:

   ```bash
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
   dotnet add package Microsoft.EntityFrameworkCore.Design
   dotnet new tool-manifest
   dotnet tool install dotnet-ef
   ```

2. Change `UseInMemoryDatabase("app")` to `UseNpgsql`, keeping EF Core's migration history in the
   application's schema:

   ```csharp fragment
   builder.Services.AddDbContextFactory<AppDbContext>(options =>
       options.UseNpgsql(
           builder.Configuration.GetConnectionString("TraxDatabase"),
           npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AppSchema.Name)
       )
   );
   ```

   Without `MigrationsHistoryTable`, the history table goes to the first schema on the
   connection's `search_path`, which is `trax` when the database user is named `trax`.

3. Replace `db.Database.EnsureCreated();` with `db.Database.Migrate();`.

4. Create the first migration. `dotnet ef` runs `Program.cs` in the Development environment, so
   it reads `ConnectionStrings:TraxDatabase` from `appsettings.Development.json`:

   ```bash
   dotnet ef migrations add InitialApp --context AppDbContext
   ```

The host creates the `app` schema and its tables at startup, and applies each migration you add
after a model change.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
