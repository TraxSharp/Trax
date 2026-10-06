---
layout: default
title: Domain Data Contexts
description: "DomainDataContext, the base for your application's own EF Core context: one schema per context, registration, PostgreSQL enum columns and cross-schema reads."
parent: Effect Providers
grand_parent: Effect
nav_order: 2
---

# Domain Data Contexts

Trax's own `DataContext<T>` is the framework metadata store (it holds the `trax` tables). Your application's data is a separate concern, and `DomainDataContext<TSelf>` (in `Trax.Effect.Data`) is the recommended base for it. It encodes one rule: **one project, one PostgreSQL schema, one context**.

## The base

A domain context derives `DomainDataContext<TSelf>`, declares its single schema, and configures its owned entities. The base seals `OnModelCreating` so the cross-cutting conventions cannot be skipped or reordered: it applies the default schema (on PostgreSQL; schema-less providers like SQLite and the in-memory provider are left alone), runs your `ConfigureModel`, and applies a UTC datetime converter.

```csharp
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options), ICatalogDbContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();

    protected override string Schema => "catalog";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { /* keys, indexes, relationships */ }
}
```

Each context ships a companion `I{Name}DbContext` interface deriving `IDomainDataContext`; application code depends on the interface, never the concrete type. Put the interface in its own file, `I{Name}DbContext.cs`, in the same directory as the context: the `CompanionInterfaces` guard (`Trax.Effect.Data.Testing`) looks for that file next to the context and fails when the interface is declared inside the context's file.

## Registration and bootstrap

```csharp
// One pooled factory + a scoped resolver bound to the interface. A context that takes the
// caller cannot be pooled: see Owner-scoped contexts below.
services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseNpgsql(connectionString));

// Create the schema and tables at startup (demo convenience; use migrations in production).
await app.Services.EnsureSchemaCreatedAsync<CatalogDbContext>();
```

To serve the context's `[TraxQueryModel]` entities over GraphQL, name the concrete type on the
GraphQL builder: `AddTraxGraphQL(graphql => graphql.AddDbContext<CatalogDbContext>())`. The pooled
factory above is all it needs; see [Query Models](/docs/sdk-reference/graphql-api/query-models).

`EnsureSchemaCreatedAsync` creates the default schema with `IF NOT EXISTS`, checks which of the
model's tables already exist, and creates the rest in one transaction, each with its keys, indexes,
foreign keys and seed rows:

- **First start:** every table is created, and EF Core logs each statement at `Information`
  (`Microsoft.EntityFrameworkCore.Database.Command[20101] Executed DbCommand`).
- **Every start after that:** every table exists, so nothing runs and nothing is logged. The
  existence check reads no rows and is sent outside EF, so a missing table is not logged as a
  failed command.
- **A table added to the model later** is created on the next start; the existing ones are left
  as they are. A sequence is checked and created the same way.
- **Statements tied to no table or sequence** (a Postgres extension or enum the model declares)
  run on the context's first creation only, which is decided by its own tables, the ones in its
  schema. A table it maps from another context's schema does not count, so a context that reads
  the catalog's `books` is still created in full the first time.
- **Several hosts starting at once** against a fresh database do not race: on Postgres the check
  and the creates run under a session advisory lock, so the first host creates the tables and the
  others find them there.

A statement the database refuses (a permission failure, a name already taken by another object) is
not caught: the transaction rolls back and the exception stops the host at startup.

It compares tables by name only. A column, index or constraint added to a table that already exists
is not applied, so once a table's shape changes after its first deployment, move the context to
migrations, below.

## Moving to EF migrations

A `DomainDataContext` takes ordinary EF Core migrations. Trax's own tables are migrated separately
(by `UsePostgres`, during service registration, tracked in `trax.migrations`), so the two never
touch each other's history. Four pieces:

**1. Packages.** The EF tools need the design package in the project that holds the context, and
the `dotnet-ef` tool at the same EF Core version as the rest of the app:

```bash
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

**2. One place for the provider options**, so the app and the tools agree. Put the migrations
history table in the domain's own schema; without this it lands in `public."__EFMigrationsHistory"`,
shared by every context that does the same:

```csharp
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

public static class ShopDatabase
{
    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shop");
}
```

**3. A design-time factory.** `dotnet ef` builds the context from it instead of starting your host
(which would run Trax's migrations and the rest of `Program.cs`). The base's sealed `OnModelCreating`
is no obstacle: the tools build the model through it, schema and UTC converter included, so the
migrations carry `EnsureSchema("shop")` and every table in that schema.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class ShopDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=shop;Username=app;Password=app", ShopDatabase.Npgsql)
            .Options);
}
```

`dotnet ef migrations add` builds the model offline and does not connect to that database.

**4. Apply them at startup** (or from a deployment step) in place of `EnsureSchemaCreatedAsync`:

```csharp
builder.Services.AddDomainDataContext<IShopDbContext, ShopDbContext>(o =>
    o.UseNpgsql(connectionString, ShopDatabase.Npgsql));
builder.Services.AddTrax(trax => trax.AddEffects(e => e.UsePostgres(connectionString)) /* ... */);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShopDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}
```

Then:

```bash
dotnet ef migrations add Initial -o Data/Migrations
```

On a fresh database the first `MigrateAsync` logs one `Error` from EF Core, a failed
`SELECT ... FROM shop."__EFMigrationsHistory"` before it creates that table; later starts log none.
`MigrateAsync` refuses to start the host (`PendingModelChangesWarning`) when the model has changed
without a migration. The `NoPendingModelChanges` [architecture guard](/docs/reference/architecture-guards)
catches that in CI: list the context in `MigrationContexts`.

**A database `EnsureSchemaCreatedAsync` already created** has the tables and no history, so the
first `MigrateAsync` fails with `42P07: relation "Orders" already exists`. Generate `Initial` from the
model exactly as deployed, then record it as applied once, before the new code starts:

```sql
CREATE TABLE IF NOT EXISTS shop."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
    "ProductVersion" character varying(32) NOT NULL);
INSERT INTO shop."__EFMigrationsHistory" VALUES ('20261003182101_Initial', '10.0.12');
```

Use the migration's full id (its file name) and your EF Core version. Every later migration then
applies normally.

## PostgreSQL enum columns

A C# enum stored as a PostgreSQL enum type is mapped in the `UseNpgsql` options callback, which
is where EF Core's model learns about it:

```csharp
services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(connectionString, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

Passing a connection string, as above, is enough: EF Core builds the data source and carries the
mapping into it. If you build your own `NpgsqlDataSource` and pass that instead, map the enum on
the `NpgsqlDataSourceBuilder` as well, because EF Core cannot change a data source it did not
build. The builder mapping handles the ADO.NET layer and the callback mapping handles the model;
leaving out the callback one fails at runtime with
`column "x" is of type book_format but expression is of type integer`.

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<BookFormat>("catalog.book_format");
var dataSource = dataSourceBuilder.Build();

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(dataSource, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

`UsePostgres` does both for Trax's own enums (`TrainState`, `LogLevel`, `ScheduleType` and the
rest) on the metadata store, so this only concerns enum types your own contexts add.

## Owner-scoped contexts

A context whose rows belong to users (a member's loans, a customer's orders) narrows every query to
the caller with an EF query filter, and the filter needs the caller. That changes how the context is
built and registered, because `AddDomainDataContext` registers a **pooled** factory, and a pooled
context can only be constructed from its options: it cannot take a scoped service such as the
caller. Register such a context with an unpooled, scoped factory instead.

The context takes the caller through an interface of its own, so the data layer does not depend on
how callers are authenticated:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

public interface ILendingCaller
{
    string? PrincipalId { get; }   // the qualified principal id, or null when anonymous
    bool IsLibrarian { get; }
}

public sealed class NoLendingCaller : ILendingCaller
{
    public static readonly NoLendingCaller Instance = new();
    public string? PrincipalId => null;
    public bool IsLibrarian => false;
}

public class LendingDbContext : DomainDataContext<LendingDbContext>, ILendingDbContext
{
    private readonly ILendingCaller _caller;

    [ActivatorUtilitiesConstructor]
    public LendingDbContext(DbContextOptions<LendingDbContext> options, ILendingCaller caller)
        : base(options) => _caller = caller;

    // For tools that build the context from its options alone: it sees no owner's rows.
    public LendingDbContext(DbContextOptions<LendingDbContext> options)
        : this(options, NoLendingCaller.Instance) { }

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override string Schema => "lending";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>().HasQueryFilter(m =>
            _caller.IsLibrarian || (_caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));

        modelBuilder.Entity<Loan>().HasQueryFilter(l =>
            _caller.IsLibrarian
            || Set<Member>().Any(m => m.Id == l.MemberId
                && _caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));
    }
}
```

Register it with a scoped factory, and the interface from it:

```csharp
services.AddScoped<ILendingCaller, TraxLendingCaller>();
services.AddDbContextFactory<LendingDbContext>(o => o.UseNpgsql(connectionString), ServiceLifetime.Scoped);
services.AddScoped<ILendingDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<LendingDbContext>>().CreateDbContext());
```

Each piece is there for a reason:

- **The filter reads the caller through a field of the context.** EF Core evaluates
  `_caller.PrincipalId` when each query runs, against the context running it, so one model serves
  every request.
- **Bind the interface over [`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller)**, which never
  throws for an anonymous caller and reads the principal on each access. A principal authenticated
  after the context was built (Trax authenticates multi-scheme GraphQL requests just before they
  execute) still applies. `TraxCaller` is registered by every Trax auth scheme; a host whose scheme
  is registered only in some environments also calls `AddTraxPrincipalAccessor()`.
- **`ServiceLifetime.Scoped` on the factory.** A singleton factory resolves the context's
  constructor from the root container, where the scoped caller cannot be resolved. A scoped one
  builds each context with the caller of the request resolving it. `AddDbContextFactory` also
  registers `LendingDbContext` itself as scoped, which is what `AddTraxGraphQL().AddDbContext<LendingDbContext>()`
  resolves for the query models, so they read through the same filters as the trains.
- **Two constructors.** The [architecture guards](/docs/reference/architecture-guards) build every
  context in `DomainContexts` offline from its options alone, so an options-only constructor must
  exist; it passes a caller who sees nothing, so anything that builds the context that way fails
  closed. With two constructors, `[ActivatorUtilitiesConstructor]` tells the container and EF's
  factory which to use; without it the factory throws "Multiple constructors accepting all given
  argument types have been found".

A read that must see past the filter (whether anyone has a book out, a seed that runs at startup
with no caller) calls `IgnoreQueryFilters()` on that one query. The owner-scope census reports
every such call unless the file is listed in its `FilterBypassAllowlist` with a reason.

The [Bookworm sample](/docs/samples/bookworm) is a working owner-scoped context, with the census
adopted and cross-user E2E tests.

## Cross-schema reads

A context never references another domain. When it needs to read an entity owned by another schema, the foreign entity exposes a static `OnCrossSchemaModelCreating(ModelBuilder, string schema)` that pins it to the foreign schema and **ignores every navigation**, so EF Core never walks the foreign model graph into the consuming context. The entity is exposed there only through a scalar-only `I{Entity}Reference : IEntityReference` interface, which keeps it out of GraphQL discovery so the owning domain stays the single GraphQL owner. Relationships that cross schemas at the GraphQL layer are resolved by [cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) instead.

These conventions can be enforced in CI with the [architecture guard packages](/docs/reference/architecture-guards).

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards) | [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context)
