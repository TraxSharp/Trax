---
title: "18.2 Migrate the shop's schema"
description: "Chapter 18 of the Trax book, section 2: Migrate the shop's schema."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/deploy/the-shape
---

# 18.2 Migrate the shop's schema

Since chapter 4, `EnsureSchemaCreatedAsync` has created the shop's tables. It creates what is
missing and changes nothing that exists, which suits a schema that is still being written. A
deployed shop's schema changes under data, so it moves to EF Core migrations. Trax's own tables are
migrated separately, by `UsePostgres` when each process starts, and the two never touch each
other's history.

Add the EF tools, at the version of EF Core the rest of Shopfront uses:

```bash
dotnet tool install dotnet-ef --version 10.0.12
```

`Directory.Packages.props`:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL.PersistedOperations" Version="1.61.0" />
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Broadcaster.RabbitMQ" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Progress" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Persistence" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Scheduler" Version="1.61.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

`Microsoft.EntityFrameworkCore.Design` goes in the library that holds the context. `PrivateAssets`
keeps it out of everything that references the library: only the tools use it.

`Shopfront.Core/Shopfront.Core.csproj`:

```xml file=Shopfront.Core/Shopfront.Core.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- The two the web project's implicit usings gave the code that moved here. -->
    <Using Include="Microsoft.Extensions.DependencyInjection" />
    <Using Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Trax.Api.Auth" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.StateMachine.Persistence" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>

  <ItemGroup>
    <!-- Each machine's committed corpus, which it replays to check itself. -->
    <EmbeddedResource
      Include="..\machines\checkout\differential.json"
      LogicalName="machines/checkout/differential.json"
      Condition="Exists('..\machines\checkout\differential.json')"
    />
  </ItemGroup>
</Project>
```

The provider options the hub, the tests and the tools use are written once. They put the
migrations' history in the `shop` schema, beside the shop's tables.

`Shopfront.Core/Data/ShopfrontDatabase.cs`:

```csharp compile=app file=Shopfront.Core/Data/ShopfrontDatabase.cs
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Shopfront.Data;

// The provider options the hub, the tests and the EF tools share.
public static class ShopfrontDatabase
{
    // The migrations' history goes in the shop's own schema, beside its tables.
    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shop");
}
```

`dotnet ef` builds the context from a design-time factory rather than starting the hub, which
would migrate Trax's tables and run the rest of `Program.cs`.

`Shopfront.Core/Data/ShopfrontDbContextFactory.cs`:

```csharp compile=app file=Shopfront.Core/Data/ShopfrontDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopfront.Data;

// What dotnet ef builds the context from, instead of starting the hub. It reads the model and
// connects to nothing.
public class ShopfrontDbContextFactory : IDesignTimeDbContextFactory<ShopfrontDbContext>
{
    public ShopfrontDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=shopfront", ShopfrontDatabase.Npgsql)
                .Options
        );
}
```

`Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Couriers;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Warehouse;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself. Every process that runs the
    // trains calls it, because Trax checks every train's chain when a process starts.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString, ShopfrontDatabase.Npgsql),
                ServiceLifetime.Scoped
            )
            .AddScoped<IShopfrontDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>().CreateDbContext()
            )
            .AddScoped<IShopfrontCaller, TraxShopfrontCaller>()
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>()
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>())
            .AddSingleton<IWarehouse, FakeWarehouse>()
            .AddSingleton<ICourier, FakeCourier>();
}
```

Generate the first migration:

```bash
dotnet ef migrations add Initial --project Shopfront.Core --output-dir Data/Migrations
```

```text
Shopfront.Core/Data/Migrations/
├── 20261018214548_Initial.cs
├── 20261018214548_Initial.Designer.cs
└── ShopfrontDbContextModelSnapshot.cs
```

Commit them. Every later change to the model is a migration of its own, `dotnet ef migrations add`
with a name that says what changed.

Your Development database was created by `EnsureSchemaCreatedAsync`, so it has the tables and no
history, and the first migration would fail on tables that already exist. Drop it and let the hub
create it again, or [record the first migration as applied](/docs/effect/move-a-domain-data-context-to-ef-migrations#a-database-ensureschemacreatedasync-already-created).

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.3 The hub](/docs/book/deploy/the-hub)
