---
title: "17.5 Test it"
description: "Chapter 17 of the Trax book, section 5: Test it."
type: tutorial
area: general
parent: "17. Require persisted operations"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/require-persisted-operations/what-stops-working
---

# 17.5 Test it

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
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Data.Testing" />
    <PackageReference Include="Trax.Effect.StateMachine.Testing" />
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
using Trax.Effect.StateMachine;

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

Next: [18. Deploy](/docs/book/deploy)
