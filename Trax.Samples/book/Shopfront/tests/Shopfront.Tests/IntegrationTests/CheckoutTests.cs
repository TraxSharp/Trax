using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
using Trax.Core.Extensions;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class CheckoutTests
{
    private WebApplicationFactory<Program>? _app;

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
    public async ValueTask StopTheApplication()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }

    private HttpClient SignedIn(string user)
    {
        var client = _app.NotNull().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, Roles.Customer, TimeProvider.System)
        );
        return client;
    }

    private static async Task<JsonElement> Send(HttpClient client, string id, string mutation)
    {
        var response = await client
            .Post(
                $"mutation($id: UUID!) {{ machines {{ checkout {{ {mutation} {{ state problem {{ code message }} }} }} }} }}",
                new { id }
            )
            .ConfigureAwait(false);
        return response.GetProperty("data").GetProperty("machines").GetProperty("checkout");
    }

    private static async Task<string> Start(HttpClient client)
    {
        var response = await client
            .Post("mutation { machines { checkout { start(requestId: \"s\") { id } } } }")
            .ConfigureAwait(false);
        return response
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty("checkout")
            .GetProperty("start")
            .GetProperty("id")
            .GetString()
            .NotNull();
    }

    [Test]
    public async Task Checkout_EveryStep_ReachesPlacing()
    {
        var ada = SignedIn("ada");
        var id = await Start(ada);

        await Send(
            ada,
            id,
            "setLines(id: $id, input: { lines: [{ sku: \"MUG\", quantity: 2 }] }, requestId: \"a\")"
        );
        await Send(
            ada,
            id,
            "setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"b\")"
        );
        await Send(
            ada,
            id,
            "choosePayment(id: $id, input: { paymentToken: \"tok_visa\" }, requestId: \"c\")"
        );
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

        hers.GetProperty("setLines")
            .GetProperty("state")
            .ValueKind.Should()
            .Be(JsonValueKind.Null, "a checkout is its owner's");
    }
}
