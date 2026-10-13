---
title: Adopt the architecture guards
description: "Add the Trax architecture guards to a test project: subclass the guard fixtures, configure the owner-scope census, and check the database after each test."
type: how-to
area: tooling
nav_order: 7
source:
  - Trax.Core/src/Trax.Core.Testing
  - Trax.Effect/src/Trax.Effect.Data.Testing
  - Trax.Api/src/Trax.Api.GraphQL.Testing
  - Trax.Mediator/src/Trax.Mediator.Testing
---

# Adopt the architecture guards

What each guard checks, and every option, is on [Architecture Guards](/docs/reference/architecture-guards).
The [Bookworm](/docs/samples/bookworm) sample subclasses all three fixtures, owner-scope census
included, in `Trax.Samples/tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs`, and
the [Recovery](/docs/samples/recovery) suites run the database check after each test.

## Subclass the fixtures

1. Reference the guard packages for the concerns you have from an NUnit test project:
   `Trax.Effect.Data.Testing` for the data layer, `Trax.Api.GraphQL.Testing` for cross-schema
   GraphQL edges, `Trax.Mediator.Testing` for trains. Each brings `Trax.Core.Testing` with it.

2. Subclass the fixture for each concern and supply its configuration. The fixtures carry the
   `[Test]` methods; you write no test bodies.

   ```csharp compile
   using System.Reflection;
   using NUnit.Framework;
   using Trax.Api.GraphQL.DataLoaders.CrossSchema;
   using Trax.Api.GraphQL.Testing;
   using Trax.Core.Testing;
   using Trax.Effect.Data.Testing;
   using Trax.Mediator.Testing;

   [TestFixture]
   public sealed class MyDataLayerGuards : DomainDataLayerGuardFixture
   {
       protected override ArchitectureGuardOptions Options =>
           new() { SourceScanRoots = ["libs", "apps"] };

       protected override IReadOnlyList<Type> DomainContexts =>
           [typeof(CatalogDbContext), typeof(LendingDbContext)];

       // Only contexts that use EF migrations; omit any bootstrapped with EnsureSchemaCreatedAsync.
       protected override IReadOnlyList<Type> MigrationContexts => [typeof(CatalogDbContext)];
   }

   [TestFixture]
   public sealed class MyCrossSchemaGuards : CrossSchemaGuardFixture
   {
       protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs"] };

       protected override IReadOnlyList<CrossSchemaEdge> Edges => MyCrossSchemaEdges.All;
   }

   [TestFixture]
   public sealed class MyTrainGuards : TrainGuardFixture
   {
       protected override IReadOnlyList<Assembly> TrainAssemblies =>
           [typeof(MyAssemblyMarker).Assembly];
   }
   ```

   Keep `[TestFixture]` on each subclass: the runner discovers the inherited tests through it.
   Subclass only the fixtures for concerns you have.

3. Run `dotnet test`. A guard pointed at a folder that holds nothing fails rather than passing, so
   a first failure that names the scan roots usually means `SourceScanRoots` is wrong: the roots are
   relative to the folder holding the nearest `*.slnx` above the test assembly.

4. If the repository has no cross-schema resolvers, or no `[Parent]` resolvers, override
   `ExpectsCrossSchemaResolvers` or `ExpectsParentResolvers` on `MyCrossSchemaGuards` to return
   `false`.

## Configure the owner-scope census

For a context whose rows belong to a user, give the data-layer fixture the context's model and the
types the census reads. Building the model needs no database, only a provider:

```csharp compile
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;
using Trax.Core.Testing;
using Trax.Effect.Data.Testing;

[TestFixture]
public sealed class MyDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options =>
        new() { SourceScanRoots = ["libs", "apps"] };

    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ClientDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new ClientDbContext(options, new SystemPrincipal());
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(UserProfile),
            PrincipalAccessorType = typeof(IPrincipalAccessor),
            NavigationScoped = new Dictionary<Type, string>
            {
                [typeof(PollQuestionAnswer)] = "belongs to the PollAnswer that holds the UserId",
            },
        };
}
```

1. Set `OwnerType` to the type whose key marks a row as someone's, and `PrincipalAccessorType` to
   the type your query filters read the caller from.
2. Run the tests. Each failure names an entity: add the missing query filter, declare an entity
   that reaches its owner through a navigation in `NavigationScoped`, list a deliberate role or
   policy gate in `Gated`, or leave an entity out with `Exemptions`. Each of the last three takes a
   written reason.
3. When a file switches the owner scope off on purpose, list it with the reason in
   `FilterBypassAllowlist`:

   ```csharp compile
   using NUnit.Framework;
   using Trax.Core.Testing;
   using Trax.Effect.Data.Testing;

   [TestFixture]
   public sealed class MyDataLayerGuards : DomainDataLayerGuardFixture
   {
       protected override ArchitectureGuardOptions Options =>
           new() { SourceScanRoots = ["libs", "apps"] };

       protected override OwnerScopeCensusOptions OwnerScope =>
           new()
           {
               OwnerType = typeof(UserProfile),
               PrincipalAccessorType = typeof(IPrincipalAccessor),
               FilterBypassAllowlist = new Dictionary<string, string>
               {
                   ["apps/Worker/Trains/EraseAccount/EraseAccountTrain.cs"] =
                       "erasing an account deletes that owner's rows from a system context",
               },
           };
   }
   ```

4. Write a cross-user test for each per-user entity, against the running API: sign in as one
   user, create a row, sign in as a second user, and assert that every surface the row can be read
   through (each GraphQL query and model, each train that returns it) gives the second user
   nothing. The census proves a filter exists, not that it compares the right column; this test is
   what exercises the filter as it runs.

## Check the database after each test

Call `TraxInvariants` from teardown, after every host the test started has been disposed, and skip
the tests that leave a stuck state on purpose:

```csharp compile
using NUnit.Framework;
using Trax.Effect.Data.Testing;

public abstract class DatabaseTest
{
    protected abstract string ConnectionString { get; }

    [TearDown]
    public async Task The_database_is_left_consistent()
    {
        // Dispose the hosts the test started before this runs.
        if (TraxInvariants.IsExempt(GetType(), TestContext.CurrentContext.Test.MethodName))
            return;

        await TraxInvariants.AssertConsistentAsync(ConnectionString);
    }
}
```

A test that leaves a run or a claim behind on purpose, such as a crash simulation, carries
`[LeavesStuckRuns("why")]` on the method or the fixture.

## SDK Reference

> [DomainDataContext](/docs/effect/effect-providers/domain-data-contexts) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Query models](/docs/sdk-reference/graphql-api/query-models)
