using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Bakery.Api.Auth;
using Trax.Samples.Bakery.E2E.Fixtures;

namespace Trax.Samples.Bakery.E2E;

/// <summary>
/// The same host started in Production: the sign-in endpoint is not mapped, a token signed with the demo key reads
/// nothing, and a hand-written document is refused even with <c>Bakery:RequirePersistedOperations</c> off. Each read
/// is checked against the Development host first, so a refusal here is the posture's and not a wrong request.
/// </summary>
[TestFixture]
public class ProductionPostureTests
{
    private sealed class ProductionFactory : BakeryApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    private BakeryApiFactory _development = null!;
    private ProductionFactory _production = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _development = new BakeryApiFactory();
        _production = new ProductionFactory();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _production.DisposeAsync();
        await _development.DisposeAsync();
    }

    [Test]
    public async Task No_token_is_minted_in_Production()
    {
        var signIn = new { name = "Ada", role = "customer" };

        using var development = await _development
            .CreateClient()
            .PostAsJsonAsync("/dev/token", signIn);
        development.StatusCode.Should().Be(HttpStatusCode.OK);

        using var production = await _production
            .CreateClient()
            .PostAsJsonAsync("/dev/token", signIn);
        production.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_demo_token_reads_no_view_in_Production()
    {
        var body = new { id = OrdersViewId(), variables = new { } };
        var token = DemoTokens.Mint("Ada", "customer");

        using var development = await PostAsync(_development, body, token);
        development.RootElement.TryGetProperty("errors", out _).Should().BeFalse(Show(development));

        using var production = await PostAsync(_production, body, token);
        production.RootElement.TryGetProperty("errors", out _).Should().BeTrue(Show(production));
        Orders(production).Should().BeNull(Show(production));
    }

    [Test]
    public async Task A_hand_written_document_is_refused_in_Production()
    {
        // Anyone may ask for __typename, so only the document's being hand-written can refuse it.
        var body = new { query = "{ __typename }" };

        using var development = await PostAsync(_development, body, token: null);
        development.RootElement.TryGetProperty("errors", out _).Should().BeFalse(Show(development));

        using var production = await PostAsync(_production, body, token: null);
        production.RootElement.TryGetProperty("errors", out _).Should().BeTrue(Show(production));
        production.RootElement.TryGetProperty("data", out _).Should().BeFalse(Show(production));
    }

    private static string OrdersViewId()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "client", "manifest.json"))
        );
        return manifest
            .RootElement.GetProperty("views")
            .EnumerateArray()
            .Single(v => v.GetProperty("operation").GetString() == "orders")
            .GetProperty("id")
            .GetString()!;
    }

    private static async Task<JsonDocument> PostAsync(
        BakeryApiFactory factory,
        object body,
        string? token
    )
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(body),
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement? Orders(JsonDocument answer) =>
        answer.RootElement.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("views", out var views)
        && views.ValueKind == JsonValueKind.Object
        && views.TryGetProperty("orders", out var orders)
        && orders.ValueKind != JsonValueKind.Null
            ? orders
            : null;

    private static string Show(JsonDocument answer) => answer.RootElement.GetRawText();
}
