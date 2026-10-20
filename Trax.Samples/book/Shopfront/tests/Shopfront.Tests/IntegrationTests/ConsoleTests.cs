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
