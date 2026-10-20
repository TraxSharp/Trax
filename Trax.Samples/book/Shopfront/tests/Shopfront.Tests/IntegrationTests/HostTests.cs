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
