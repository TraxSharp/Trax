---
title: "8.7 Test it"
description: "Chapter 8 of the Trax book, section 7: Test it."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/the-operations-console/a-customer-gets-nothing
---

# 8.7 Test it

The host tests check the gate both ways, and that the console is served.

`tests/Shopfront.Tests/IntegrationTests/ConsoleTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/ConsoleTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ConsoleTests
{
    private const string Executions =
        "{ operations { executions(take: 10) { items { name trainState } } } }";

    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private HttpClient SignedIn(string user, string role)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, role)
        );
        return client;
    }

    [Test]
    public async Task Console_IsServed()
    {
        var response = await _app.CreateClient().GetAsync("/trax/dashboard/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Executions_Staff_ReadEveryRun()
    {
        await SignedIn("ada", Roles.Customer)
            .Post(
                "mutation P($input: PlaceOrderInput!) { dispatch { placeOrder(input: $input) { externalId } } }",
                new
                {
                    input = new
                    {
                        paymentToken = "tok_visa",
                        lines = new[] { new { sku = "MUG", quantity = 1 } },
                    },
                }
            );

        var response = await SignedIn("lin", Roles.Staff).Post(Executions);

        response.ErrorCodes().Should().BeEmpty();
        response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("executions")
            .GetProperty("items")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .Should()
            .Contain("Shopfront.Trains.PlaceOrder.IPlaceOrderTrain");
    }

    [Test]
    public async Task Executions_Customer_IsNotAuthorized()
    {
        var response = await SignedIn("ada", Roles.Customer).Post(Executions);

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task Executions_Anonymous_IsNotAuthorized()
    {
        var response = await _app.CreateClient().Post(Executions);

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    55, Skipped:     0, Total:    55, Duration: 1 m 8 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Staff can read everything Trax recorded about every run, in a console that reaches it through the
same schema the storefront will use, behind a gate that customers and anonymous callers cannot
pass.

That ends the API part. Shopfront answers customers as they ask, and staff can see what happened.
The next part adds work nobody asks for: jobs that run on a schedule, retry when they fail, and
run on a process of their own.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)

---

Next: [9. Scheduled work](/docs/book/scheduled-work)
