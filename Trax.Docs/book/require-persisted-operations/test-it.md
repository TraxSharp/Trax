---
title: "18.5 Test it"
description: "Chapter 18 of the Trax book, section 5: Test it."
type: tutorial
area: general
parent: "18. Require persisted operations"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/require-persisted-operations/what-stops-working
---

# 18.5 Test it

The host tests write their documents as text. A fixture for the folder sets the Development-only
switch for the whole run:

`tests/Shopfront.Tests/IntegrationTests/DocumentsAsText.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/DocumentsAsText.cs
namespace Shopfront.Tests.IntegrationTests;

// The host tests write their documents as text, which the hub refuses. For the whole run they set
// the Development-only switch that lets text run; a test that checks the refusal sets it back.
[SetUpFixture]
public class DocumentsAsText
{
    private const string Switch = "PersistedOperations__Require";

    [OneTimeSetUp]
    public void LetTextRun() => Environment.SetEnvironmentVariable(Switch, "false");

    [OneTimeTearDown]
    public void Restore() => Environment.SetEnvironmentVariable(Switch, null);
}
```

The returns features (chapter 14) need no switch. The Trax bindings send each step by the
persisted id the client manifest holds for it, so their hub runs as it ships. The hook that starts it
reads the manifest from the copy the test project makes (below):

`tests/Shopfront.Tests/FeatureHooks.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/FeatureHooks.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shopfront.Auth;
using Shopfront.Data;
using Trax.StateMachine;
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
        // The hub runs only the documents it registered, so the bindings send each step by the
        // persisted id the storefront's client manifest holds for it.
        var manifest = Path.Combine(AppContext.BaseDirectory, "manifest.json");

        // The lexicon is read with the trains' records, as the hub reads them.
        var lexicon = Lexicon.ReadDirectory(
            features,
            [typeof(Shopfront.Trains.ReturnOrder.IReturnOrderTrain).Assembly]
        )!;

        TraxFeatures.Configure(trax =>
        {
            trax.Host = hub.Services;
            trax.Client = client;
            trax.Glossary = lexicon.GlossaryOrThrow();
            trax.Manifest = ClientManifest.Read(File.ReadAllText(manifest));
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

One test class checks the refusal itself, with the switch set back. It needs a registered
document's id: the client manifest the storefront is generated from holds the checkout's, and the
test project copies it beside the tests.

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
    <!-- The client manifest the storefront is generated from, for the persisted ids it holds. -->
    <None
      Include="..\..\web\src\trax\manifest.json"
      Link="manifest.json"
      CopyToOutputDirectory="PreserveNewest"
    />
  </ItemGroup>

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

`tests/Shopfront.Tests/IntegrationTests/PersistedOperationsTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PersistedOperationsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
using Trax.StateMachine;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PersistedOperationsTests
{
    private static readonly ClientManifest Manifest = ClientManifest.Read(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json"))
    );

    private static readonly ClientManifestDocument StartCheckout = Manifest
        .Machines.Single(m => m.Id == "checkout")
        .Documents.Single(d => d.Operation == "start");

    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
                // As deployed: only registered documents run.
                .UseSetting("PersistedOperations:Require", "true")
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private HttpClient Ada()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("ada", Roles.Customer)
        );
        return client;
    }

    [Test]
    public async Task ARegisteredDocument_SentByItsId_Runs()
    {
        using var ada = Ada();

        var response = await ada.PostAsJsonAsync(
            "/trax/graphql",
            new { id = StartCheckout.Id, variables = new { requestId = "s" } }
        );

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("checkout")
            .GetProperty("start")
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("Cart");
    }

    [Test]
    public async Task TheSameDocument_SentAsText_IsRefused()
    {
        using var ada = Ada();

        var response = await ada.PostAsJsonAsync(
            "/trax/graphql",
            new { query = StartCheckout.Document, variables = new { requestId = "s" } }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ErrorCodes().Should().Equal("PERSISTED_OPERATION_REQUIRED");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    76, Skipped:     0, Total:    76 - Shopfront.Tests.dll (net10.0)
```

## What we have

The hub runs the documents it registered and nothing else: the storefront's, the ones it generates
for its machines and for following runs, and the console's. A document sent as text is refused in
every environment, unless a developer turns that off on their own machine.

The last chapter deploys Shopfront.

## SDK Reference

> [Turn on persisted operations](/docs/api/turn-on-persisted-operations) | [Persisted operations](/docs/api/persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)

---

Next: [19. Deploy](/docs/book/deploy)
