---
title: "14.9 Test it"
description: "Chapter 14 of the Trax book, section 9: Test it."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/returns-are-written-as-features/the-board
---

# 14.9 Test it

The feature runs twice: in memory on the committed machine, and against the hub through Reqnroll.
The train has tests of its own.

## In memory

```bash
dotnet trax machine run --features features --machines machines/from-features
```

```text
returns.feature:9 A return says which order and why  passed
returns.feature:14 A return with a reason is sent to be checked  passed
returns.feature:20 A return with no reason is refused  passed
returns.feature:29 The shop accepts the return of Ada's own order  passed
returns.feature:38 The shop refuses the return of an order that is not Ada's  passed
returns.feature:47 A check that fails leaves the return unchecked  passed
returns.feature:55 A check called off leaves the return unchecked  passed
7 scenario(s): 7 passed
```

It runs on the committed files, so it tests what the hub serves, and answers as the hub does,
access included. The train is not run: each outcome answers with the output its `when` pins,
`accepted` true or false. The test project runs the same, and checks that the committed machine and
`glossary.json` are what the feature and the lexicon derive today. It reads the lexicon with the
train's assembly, so the `Returns:` line is also checked against `ReturnOrderOutput`:

`tests/Shopfront.Tests/UnitTests/ReturnFeaturesTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ReturnFeaturesTests.cs
using Shopfront.Trains.ReturnOrder;
using Trax.StateMachine.Gherkin;

namespace Shopfront.Tests.UnitTests;

// The returns features, run in memory on the machines committed in machines/from-features, as
// trax machine run runs them, and those machines and the glossary are what the features and the
// lexicon derive today. The lexicon is read with the train's records, so its Returns: line is
// checked against the train's output here (ADR Trax.Docs/adr/0059).
[TestFixture]
public class ReturnFeaturesTests
{
    private static string Features => Path.Combine(AppContext.BaseDirectory, "features");

    private static string Machines => Path.Combine(AppContext.BaseDirectory, "machines");

    private static LexiconReading Lexicon =>
        Trax.StateMachine.Gherkin.Lexicon.ReadDirectory(
            Features,
            [typeof(IReturnOrderTrain).Assembly]
        )!;

    private static FeatureGlossary Glossary =>
        Lexicon.Glossary ?? throw new InvalidOperationException(string.Join("\n", Lexicon.Problems));

    private static FeatureFile[] Files =>
        [
            .. Directory
                .GetFiles(Features, "*.feature")
                .Order(StringComparer.Ordinal)
                .Select(p => new FeatureFile(Path.GetFileName(p), File.ReadAllText(p))),
        ];

    [Test]
    public void EveryScenario_PassesOnTheCommittedMachines()
    {
        var irs = Directory
            .GetFiles(Machines, "*.ir.json")
            .ToDictionary(p => Path.GetFileName(p)[..^".ir.json".Length], File.ReadAllText);

        var report = FeatureMachines.Run(
            Glossary,
            Files,
            irs,
            File.ReadAllText(Path.Combine(Machines, "host.json"))
        );

        report.Problems.Should().BeEmpty();
        report
            .Scenarios.Where(s => s.Status != FeatureStatus.Passed)
            .Select(s => $"{s.File}:{s.Line} {s.Name}")
            .Should()
            .BeEmpty();
    }

    [Test]
    public void TheCommittedMachines_AreWhatTheFeaturesDerive()
    {
        var derived = FeatureMachines.Derive(Glossary, Files);

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            Read($"{id}.ir.json").Should().Be(ir, "run trax machine from-gherkin again");
        Read("host.json").Should().Be(derived.Host, "run trax machine from-gherkin again");
        Read("glossary.json")
            .Should()
            .Be(Lexicon.Json!.TrimEnd('\n'), "run trax machine from-gherkin again");
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(Machines, file)).Replace("\r\n", "\n").TrimEnd('\n');
}
```

## Against the hub

The test project takes the Trax bindings for Reqnroll, which hold one binding for every step the
lexicon's phrases read. There is no step code.

`Directory.Packages.props`:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.GraphQL.PersistedOperations" Version="1.69.0" />
    <PackageVersion Include="Trax.Core" Version="1.69.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Progress" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.69.0" />
    <PackageVersion Include="Trax.StateMachine.Gherkin.Reqnroll" Version="1.69.0" />
    <PackageVersion Include="Trax.StateMachine.Persistence" Version="1.69.0" />
    <PackageVersion Include="Trax.StateMachine.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.69.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Scheduler" Version="1.69.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <!-- NUnit3TestAdapter pulls these at versions deprecated as Legacy; pin newer ones. -->
    <PackageVersion Include="Microsoft.Testing.Extensions.VSTestBridge" Version="2.5.1" />
    <PackageVersion Include="Microsoft.Testing.Platform.MSBuild" Version="2.5.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
    <PackageVersion Include="Reqnroll.NUnit" Version="3.3.4" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/Shopfront.Tests.csproj`:

```xml file=tests/Shopfront.Tests/Shopfront.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shopfront.Hub\Shopfront.Hub.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- The returns features, run against the hub by the Trax bindings, and the lexicon they
         read. Reqnroll writes a test class for each feature file. -->
    <ReqnrollFeatureFiles Include="..\..\features\*.feature" Link="Features\%(Filename)%(Extension)" />
    <None Include="..\..\features\*" LinkBase="features" CopyToOutputDirectory="PreserveNewest" />
    <None Include="reqnroll.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Reqnroll.NUnit" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Data.Testing" />
    <PackageReference Include="Trax.StateMachine.Gherkin.Reqnroll" />
    <PackageReference Include="Trax.StateMachine.Testing" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/reqnroll.json`:

```json file=tests/Shopfront.Tests/reqnroll.json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "bindingAssemblies": [{ "assembly": "Trax.StateMachine.Gherkin.Reqnroll" }]
}
```

One hook starts the hub, reads the lexicon for the bindings, and tells them how a person signs in.

`tests/Shopfront.Tests/FeatureHooks.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/FeatureHooks.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shopfront.Auth;
using Shopfront.Data;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Shopfront.Tests;

// Starts the hub once for the features and points the Trax bindings at it. Every person of every
// scenario signs in as a user of their own, so no scenario sees another's returns.
[Binding]
public sealed class FeatureHooks
{
    // A database of their own, new for each run: the other tests drop theirs before each test,
    // which would pull it out from under a running hub.
    private const string Database =
        "Host=localhost;Database=shopfront_features;Username=postgres;Password=shopfront";

    private static WebApplicationFactory<Program>? hub;

    [BeforeTestRun]
    public static async Task StartTheHub()
    {
        await using (var db = Context())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        hub = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development").UseSetting("ConnectionStrings:Shopfront", Database)
        );
        var client = hub.CreateClient();
        var features = Path.Combine(AppContext.BaseDirectory, "features");

        // The lexicon is read with the trains' records, as the hub reads them.
        var lexicon = Lexicon.ReadDirectory(
            features,
            [typeof(Shopfront.Trains.ReturnOrder.IReturnOrderTrain).Assembly]
        )!;

        TraxFeatures.Configure(trax =>
        {
            trax.Host = hub.Services;
            trax.Client = client;
            trax.Glossary = lexicon.Glossary!;
            trax.Credential = (actor, person, scenario) =>
                new FeatureCredential(
                    new Dictionary<string, string>
                    {
                        ["Authorization"] =
                            "Bearer " + DevJwt.Mint($"{person}-{scenario}", actor.Roles[0]),
                    }
                );
        });
    }

    // The hub hands every job to the worker, and these tests start none: a scenario that waits for
    // the check's answer runs in memory only (ReturnFeaturesTests).
    [BeforeScenario("in-memory")]
    public static void InMemoryOnly() =>
        Assert.Ignore("Runs in memory only: no worker answers the check here.");

    [AfterTestRun]
    public static async Task StopTheHub()
    {
        if (hub is not null)
            await hub.DisposeAsync();
    }

    private static ShopfrontDbContext Context() =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>().UseNpgsql(Database).Options,
            TestCaller.Staff
        );
}
```

- **Each person is a user of their own.** The bindings call `Credential` with the person and the
  scenario, so `Ada` in one scenario is not `Ada` in the next, and no scenario sees another's
  returns.
- **A Given is written through the hub's own store.** It fires nothing. Everything a person does
  goes over GraphQL with their token, so a trigger the hub does not serve them is refused as it
  would be for the storefront.
- **`@in-memory` scenarios are skipped here.** The hub hands every job to the worker, and these
  tests start none, so nothing would answer the check. Those scenarios run in memory above.

## The check

`tests/Shopfront.Tests/UnitTests/CheckTheOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/CheckTheOrderJunctionTests.cs
using Shopfront.Data.Models;
using Shopfront.Trains.ReturnOrder;
using Shopfront.Trains.ReturnOrder.Junctions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class CheckTheOrderJunctionTests
{
    private static readonly Guid AdasOrder = Guid.NewGuid();

    [SetUp]
    public async Task PlaceAdasOrder()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        db.Orders.Add(
            new Order
            {
                Id = AdasOrder,
                CustomerId = "TraxJwt:ada",
                Total = 24.00m,
                TransactionId = "tx_test",
                PlacedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<bool> Check(string orderId, string customerId)
    {
        await using var db = TestDatabase.NewContext();
        var output = await new CheckTheOrderJunction(db).Run(
            new ReturnOrderInput { OrderId = orderId, CustomerId = customerId }
        );
        return output.Accepted;
    }

    [Test]
    public async Task Run_HerOwnOrder_IsAccepted() =>
        (await Check(AdasOrder.ToString(), "TraxJwt:ada")).Should().BeTrue();

    [Test]
    public async Task Run_SomeoneElsesOrder_IsRefused() =>
        (await Check(AdasOrder.ToString(), "TraxJwt:bo")).Should().BeFalse();

    [Test]
    public async Task Run_NotAnOrderId_IsRefused() =>
        (await Check("not-an-order", "TraxJwt:ada")).Should().BeFalse();
}
```

```bash
dotnet test
```

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [15. Checks in parallel, and resuming](/docs/book/checks-in-parallel-and-resuming)
