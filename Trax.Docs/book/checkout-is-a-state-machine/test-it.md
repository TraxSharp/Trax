---
title: "12.9 Test it"
description: "Chapter 12 of the Trax book, section 9: Test it."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/checkout-is-a-state-machine/export-the-definition
---

# 12.9 Test it

The tests check the machine against the committed files, and drive a checkout through the API as
a customer would.

`tests/Shopfront.Tests/UnitTests/CheckoutMachineTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/CheckoutMachineTests.cs
using Shopfront.Machines.Checkout;
using Trax.Effect.StateMachine.Testing;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class CheckoutMachineTests
{
    // The solution's folder, where machines/ is.
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the tests.");
    }

    [Test]
    public void Checkout_MatchesItsExportedDefinitionAndCorpus()
    {
        var folder = Path.Combine(Root(), "machines", "checkout");

        MachineConformance
            .Check(
                new CheckoutMachine(),
                Path.Combine(folder, "checkout.ir.json"),
                Path.Combine(folder, "differential.json")
            )
            .Should()
            .BeEmpty("the machine changed without `dotnet trax machine generate`");
    }
}
```

`tests/Shopfront.Tests/IntegrationTests/CheckoutTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/CheckoutTests.cs
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class CheckoutTests
{
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

    private HttpClient SignedIn(string user)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, Roles.Customer)
        );
        return client;
    }

    private static async Task<JsonElement> Send(HttpClient client, string id, string mutation)
    {
        var response = await client.Post(
            $"mutation($id: UUID!) {{ machines {{ checkout {{ {mutation} {{ state problem {{ code message }} }} }} }} }}",
            new { id }
        );
        return response.GetProperty("data").GetProperty("machines").GetProperty("checkout");
    }

    private static async Task<string> Start(HttpClient client)
    {
        var response = await client.Post(
            "mutation { machines { checkout { start(requestId: \"s\") { id } } } }"
        );
        return response
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty("checkout")
            .GetProperty("start")
            .GetProperty("id")
            .GetString()!;
    }

    [Test]
    public async Task Checkout_EveryStep_ReachesPlacing()
    {
        var ada = SignedIn("ada");
        var id = await Start(ada);

        await Send(ada, id, "setLines(id: $id, input: { lines: [{ sku: \"MUG\", quantity: 2 }] }, requestId: \"a\")");
        await Send(ada, id, "setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"b\")");
        await Send(ada, id, "choosePayment(id: $id, input: { paymentToken: \"tok_visa\" }, requestId: \"c\")");
        var placed = await Send(ada, id, "placeOrder(id: $id, requestId: \"d\")");

        placed.GetProperty("placeOrder").GetProperty("state").GetString().Should().Be("Placing");
    }

    [Test]
    public async Task SetAddress_EmptyCart_IsRefusedWithTheReason()
    {
        var ada = SignedIn("ada");
        var id = await Start(ada);

        var refused = await Send(
            ada,
            id,
            "setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"a\")"
        );

        var answer = refused.GetProperty("setAddress");
        answer.GetProperty("problem").GetProperty("code").GetString().Should().Be("GUARD_FAILED");
        answer
            .GetProperty("problem")
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Put something in the cart and give an address to send it to.");
    }

    [Test]
    public async Task AnotherCustomer_CannotReachTheCheckout()
    {
        var id = await Start(SignedIn("ada"));

        var hers = await Send(
            SignedIn("grace"),
            id,
            "setLines(id: $id, input: { lines: [] }, requestId: \"a\")"
        );

        hers.GetProperty("setLines").GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null, "a checkout is its owner's");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    70, Skipped:     0, Total:    70, Duration: 58 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Checkout is a machine the server applies: each step a typed mutation, refused with its reason when
it breaks a rule, owned by the customer who started it, cleared by a timer when it is left alone.
Its definition is exported and checked, so the browser's copy of it can be generated from the same
source later.

Checkout stops at `Placing`. The next chapter has it place the order.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [13. The machine places the order](/docs/book/the-machine-places-the-order)
