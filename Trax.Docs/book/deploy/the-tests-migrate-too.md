---
title: "18.5 The tests migrate too"
description: "Chapter 18 of the Trax book, section 5: The tests migrate too."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/deploy/the-worker
---

# 18.5 The tests migrate too

The test database is migrated as the hub's is, so the tests run against the schema the migrations
build, and a model change without a migration fails them.

`tests/Shopfront.Tests/TestDatabase.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/TestDatabase.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Tests;

// The tests' own database, beside the application's, so a test never touches its rows.
internal static class TestDatabase
{
    public const string ConnectionString =
        "Host=localhost;Database=shopfront_tests;Username=postgres;Password=shopfront";

    // A context for a caller; staff, who read every order, unless the test says otherwise.
    public static ShopfrontDbContext NewContext(IShopfrontCaller? caller = null) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql(ConnectionString, ShopfrontDatabase.Npgsql)
                .Options,
            caller ?? TestCaller.Staff
        );

    // Drops the database and migrates a new one, as the hub does: the seeded catalogue, and no
    // orders or runs.
    public static async Task Reset()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public static async Task<int> StockOf(string sku)
    {
        await using var db = NewContext();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Stock).SingleAsync();
    }
}
```

The architecture guards check the same in CI, without a database: a context listed in
`MigrationContexts` must match its latest migration.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;
using Trax.Effect.Data.Testing;
using Trax.Mediator.Testing;

namespace Shopfront.Tests;

[TestFixture]
public sealed class ShopfrontTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(IPlaceOrderTrain).Assembly];
}

[TestFixture]
public sealed class ShopfrontHygieneGuards : HygieneGuardFixture;

[TestFixture]
public sealed class ShopfrontDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options =>
        new() { SourceScanRoots = ["Shopfront.Core/Data"] };

    protected override IReadOnlyList<Type> DomainContexts => [typeof(ShopfrontDbContext)];

    // The contexts EF migrations keep: each model must match its latest migration.
    protected override IReadOnlyList<Type> MigrationContexts => [typeof(ShopfrontDbContext)];

    // The model, built with no database and no caller, for the census to read.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new ShopfrontDbContext(options, NoShopfrontCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Order),
            PrincipalAccessorType = typeof(IShopfrontCaller),
            OwnerIdProperties = new HashSet<string> { nameof(Order.CustomerId) },
        };
}
```

Start the hub and worker with RabbitMQ running, and both log that they joined it:

```text
info: Trax.Effect.Broadcaster.RabbitMQ.RabbitMqTrainEventReceiver[0]
      RabbitMQ receiver started on exchange trax.lifecycle, queue amq.gen-cr_Uh-wKacH-AdfGlPjXyA.
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    76, Skipped:     0, Total:    76 - Shopfront.Tests.dll (net10.0)
```

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.6 Containers](/docs/book/deploy/containers)
