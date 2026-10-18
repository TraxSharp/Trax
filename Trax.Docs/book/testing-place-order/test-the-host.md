---
title: "2.5 Test the host"
description: "Chapter 2 of the Trax book, section 5: Test the host."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/testing-place-order/test-the-train
---

# 2.5 Test the host

A host test starts the whole application in memory, as `dotnet run` would, and calls it over
HTTP. It is the test that fails when `Program.cs` no longer starts or an endpoint answers with the
wrong status.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public void StartTheApplication() => _app = new WebApplicationFactory<Program>();

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    [Test]
    public async Task PostOrders_InStock_Answers200WithTheOrder()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync(
                "/orders",
                new { customerId = "ada", lines = new[] { new { sku = "MUG", quantity = 2 } } }
            );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        order!.Total.Should().Be(24.00m);
    }

    [Test]
    public async Task PostOrders_OutOfStock_Answers422WithTheReason()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync(
                "/orders",
                new { customerId = "ada", lines = new[] { new { sku = "CAP", quantity = 1 } } }
            );

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("CAP is out of stock.");
    }
}
```

`WebApplicationFactory<Program>` runs `Program.cs` with a test server in place of Kestrel. The
`InternalsVisibleTo` line is what lets the tests name `Program` at all.

The requests are anonymous objects rather than `PlaceOrderInput`, on purpose: they send the JSON a
real client sends, so a renamed property shows up here as a failing test instead of passing on
both sides.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.6 Guard the project's shape](/docs/book/testing-place-order/guard-the-projects-shape)
