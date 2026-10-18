---
title: "7.6 Test it"
description: "Chapter 7 of the Trax book, section 6: Test it."
type: tutorial
area: general
parent: "7. Serve it over GraphQL"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/serve-it-over-graphql/ask-it
---

# 7.6 Test it

The host tests speak GraphQL now. A small helper posts a query and returns the response.

`tests/Shopfront.Tests/IntegrationTests/GraphQL.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/GraphQL.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace Shopfront.Tests.IntegrationTests;

internal static class GraphQL
{
    public static async Task<JsonElement> Post(
        this HttpClient client,
        string query,
        object? variables = null
    )
    {
        var response = await client.PostAsJsonAsync("/trax/graphql", new { query, variables });
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    // The extensions.code of each error, empty when there are none.
    public static IEnumerable<string?> ErrorCodes(this JsonElement response) =>
        response.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                        ? c.GetString()
                        : null
                )
            : [];
}
```

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private const string PlaceOrder = """
        mutation Place($input: PlaceOrderInput!) {
          dispatch { placeOrder(input: $input) { externalId output { orderId total } } }
        }
        """;

    private const string Orders = "{ discover { orders { nodes { customerId total } } } }";

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

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new
        {
            input = new { paymentToken, lines = new[] { new { sku, quantity = 1 } } },
        };

    private static JsonElement Placed(JsonElement response) =>
        response.GetProperty("data").GetProperty("dispatch").GetProperty("placeOrder");

    [Test]
    public async Task Products_Anonymous_ListsTheCatalogue()
    {
        var response = await _app.CreateClient()
            .Post("{ discover { products(order: { sku: ASC }) { nodes { sku } } } }");

        response
            .GetProperty("data")
            .GetProperty("discover")
            .GetProperty("products")
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(p => p.GetProperty("sku").GetString())
            .Should()
            .Equal("CAP", "MUG", "TEE");
    }

    [Test]
    public async Task PlaceOrder_Anonymous_IsNotAuthorized()
    {
        var response = await _app.CreateClient().Post(PlaceOrder, Order("MUG"));

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task PlaceOrder_Staff_IsNotAuthorized()
    {
        var response = await SignedIn("lin", Roles.Staff).Post(PlaceOrder, Order("MUG"));

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task PlaceOrder_Customer_AnswersTheOrder()
    {
        var response = await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG"));

        Placed(response).GetProperty("output").GetProperty("total").GetDecimal().Should().Be(12.00m);
    }

    [Test]
    public async Task PlaceOrder_Declined_AnswersTheReason()
    {
        var response = await SignedIn("ada", Roles.Customer)
            .Post(PlaceOrder, Order("MUG", "tok_chargeDeclined"));

        response.ErrorCodes().Should().Contain("TRAX_TRAIN_ERROR");
        response
            .GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("The card was declined.");
    }

    [Test]
    public async Task Orders_EachCustomerSeesTheirOwn_StaffSeeAll()
    {
        await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG"));
        await SignedIn("grace", Roles.Customer).Post(PlaceOrder, Order("MUG"));

        static IEnumerable<string?> Owners(JsonElement response) =>
            response
                .GetProperty("data")
                .GetProperty("discover")
                .GetProperty("orders")
                .GetProperty("nodes")
                .EnumerateArray()
                .Select(o => o.GetProperty("customerId").GetString());

        Owners(await SignedIn("grace", Roles.Customer).Post(Orders))
            .Should()
            .Equal("TraxJwt:grace");
        Owners(await SignedIn("lin", Roles.Staff).Post(Orders))
            .Should()
            .BeEquivalentTo(["TraxJwt:ada", "TraxJwt:grace"]);
    }

    [Test]
    public async Task RunProgress_OnlyTheStarter_CanFollowTheRun()
    {
        const string progress = """
            query Progress($runId: String!) {
              runProgress(runId: $runId) { state steps { name } problem { code } }
            }
            """;
        var placed = Placed(await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG")));
        var runId = new { runId = placed.GetProperty("externalId").GetString() };

        var mine = (await SignedIn("ada", Roles.Customer).Post(progress, runId))
            .GetProperty("data")
            .GetProperty("runProgress");
        var hers = (await SignedIn("grace", Roles.Customer).Post(progress, runId))
            .GetProperty("data")
            .GetProperty("runProgress");

        mine.GetProperty("state").GetString().Should().Be("COMPLETED");
        mine.GetProperty("steps")
            .EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .Should()
            .StartWith("IdentifyCustomerJunction");
        hers.GetProperty("problem").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 27 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront serves a GraphQL schema that Trax built from what was already there: the catalogue for
anyone, `PlaceOrder` for customers, each customer's own orders, and the progress of a run for the
one customer who started it. Not a line of it repeats a rule: the roles are on the train, the rows
on the context, and the schema follows both.

Staff can read orders, but not yet what happened to them: which runs failed, where and why. The
next chapter gives them the operations console.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [8. The operations console](/docs/book/the-operations-console)
