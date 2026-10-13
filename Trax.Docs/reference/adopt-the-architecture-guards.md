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
   `[Test]` methods; you write no test bodies. Bookworm's, less the owner-scope census:

   ```csharp from=Trax.Samples/tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs
   using System.Reflection;
   using Microsoft.EntityFrameworkCore;
   using Microsoft.EntityFrameworkCore.Metadata;
   using NUnit.Framework;
   using Trax.Api.GraphQL.DataLoaders.CrossSchema;
   using Trax.Api.GraphQL.Testing;
   using Trax.Core.Testing;
   using Trax.Effect.Data.Testing;
   using Trax.Mediator.Testing;
   // ...
   [TestFixture]
   public sealed class BookwormDataLayerGuards : DomainDataLayerGuardFixture
   {
       protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

       protected override IReadOnlyList<Type> DomainContexts =>
           [typeof(CatalogDbContext), typeof(LendingDbContext)];
   // ...
   }

   [TestFixture]
   public sealed class BookwormCrossSchemaGuards : CrossSchemaGuardFixture
   {
       protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

       protected override IReadOnlyList<CrossSchemaEdge> Edges => CrossSchemaEdges.All;
   }

   [TestFixture]
   public sealed class BookwormTrainGuards : TrainGuardFixture
   {
       protected override IReadOnlyList<Assembly> TrainAssemblies =>
           [typeof(Trax.Samples.Bookworm.AssemblyMarker).Assembly];
   }
   ```

   A context that uses EF migrations also goes in `MigrationContexts`, which checks its model
   against the latest snapshot; Bookworm's contexts create their schemas with
   `EnsureSchemaCreatedAsync`, so it lists none.

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
types the census reads. Building the model needs no database, only a provider. Bookworm's lending
context, whose members see only their own loans:

```csharp from=Trax.Samples/tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs
    // The lending model, built offline (no database, no caller), for the owner-scope census.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<LendingDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new LendingDbContext(options, NoLendingCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Member),
            PrincipalAccessorType = typeof(ILendingCaller),
            FilterBypassAllowlist = new Dictionary<string, string>
            {
                [
                    "samples/Bookworm/Trax.Samples.Bookworm/Trains/Lending/BorrowBook/Junctions/BorrowBookJunction.cs"
                ] =
                    "a book's availability depends on every member's open loans; the read returns a yes or no, no row",
// ...
            },
        };
```

1. Set `OwnerType` to the type whose key marks a row as someone's, and `PrincipalAccessorType` to
   the type your query filters read the caller from. A context whose constructor takes the caller
   is built here with a caller that is no one.
2. Run the tests. Each failure names an entity: add the missing query filter, declare an entity
   that reaches its owner only through a navigation in `NavigationScoped`, list a deliberate role
   or policy gate in `Gated`, or leave an entity out with `Exemptions`. Each of the last three
   takes a written reason.
3. When a file switches the owner scope off on purpose, list it in `FilterBypassAllowlist` with
   the reason, as Bookworm does for the availability check that reads every member's loans.
4. Write a cross-user test for each per-user entity, against the running API: sign in as one
   user, create a row, sign in as a second user, and assert that every surface the row can be read
   through (each GraphQL query and model, each train that returns it) gives the second user
   nothing. The census proves a filter exists, not that it compares the right column; this test is
   what exercises the filter as it runs.

## Check the database after each test

Run `TraxInvariants` after each test, once every host the test started has finished its work, and
skip the tests that leave a stuck state on purpose. A suite that disposes its hosts per test calls
`TraxInvariants.AssertConsistentAsync(connectionString)` from `[TearDown]`. The Recovery browser
suite keeps one host and one database across tests, so it does it with an assembly-wide NUnit
action that reports only the violations each test added:

```csharp from=Trax.Samples/tests/Trax.Samples.Recovery.Browser/Fixtures/CheckTraxInvariantsAttribute.cs
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CheckTraxInvariantsAttribute : Attribute, ITestAction
{
    private static readonly InvokingState[] InvokingStates = [new("topic-map", "Building")];

    private IReadOnlyList<TraxInvariantViolation> _before = [];

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test) => _before = Find();

    public void AfterTest(ITest test)
    {
        if (
            test.Fixture is null
            || TraxInvariants.IsExempt(test.Fixture.GetType(), test.MethodName)
        )
            return;
// ...
        if (added.Count > 0)
            Assert.Fail(TraxInvariants.Describe(added));
    }

    private List<TraxInvariantViolation> Added() => Find().Except(_before).ToList();
// ...
}
```

`InvokingStates` names each machine state that invokes a train, so the check also holds those
instances to their run tokens.

A test that leaves a run or a claim behind on purpose, such as a crash simulation, carries
`[LeavesStuckRuns("why")]` on the method or the fixture.

## SDK Reference

> [DomainDataContext](/docs/effect/effect-providers/domain-data-contexts) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Query models](/docs/sdk-reference/graphql-api/query-models)
