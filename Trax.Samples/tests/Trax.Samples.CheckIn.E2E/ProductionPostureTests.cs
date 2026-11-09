using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Trax.Samples.CheckIn.Api.Auth;
using Trax.Samples.CheckIn.E2E.Fixtures;

namespace Trax.Samples.CheckIn.E2E;

/// <summary>
/// The same host started in Production: the sign-in endpoint is not mapped and a token signed with the demo key reads
/// nothing. Each read is checked against the Development host first, so a refusal here is the posture's and not a wrong
/// request. A hand-written document is refused on both: neither host runs anything but its persisted documents.
/// </summary>
[TestFixture]
public class ProductionPostureTests
{
    private sealed class ProductionFactory : CheckInApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    private CheckInApiFactory _development = null!;
    private ProductionFactory _production = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _development = new CheckInApiFactory();
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
        var signIn = new { name = "Ada", role = "passenger" };

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
        var body = new { id = BookingsViewId(), variables = new { } };
        var token = DemoTokens.Issuer.Mint("Ada", "passenger");

        using var development = await PostAsync(_development, body, token);
        development.RootElement.TryGetProperty("errors", out _).Should().BeFalse(Show(development));

        using var production = await PostAsync(_production, body, token);
        production.RootElement.TryGetProperty("errors", out _).Should().BeTrue(Show(production));
        Bookings(production).Should().BeNull(Show(production));
    }

    [Test]
    public async Task A_hand_written_document_is_refused_in_Development_and_Production()
    {
        // Anyone may ask for __typename, so only the document's being hand-written can refuse it.
        var body = new { query = "{ __typename }" };

        foreach (var factory in new[] { _development, _production })
        {
            using var answer = await PostAsync(factory, body, token: null);
            answer.RootElement.TryGetProperty("data", out _).Should().BeFalse(Show(answer));
            answer
                .RootElement.GetProperty("errors")[0]
                .GetProperty("extensions")
                .GetProperty("code")
                .GetString()
                .Should()
                .Be("PERSISTED_OPERATION_REQUIRED", Show(answer));
        }
    }

    private static string BookingsViewId()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "client", "manifest.json"))
        );
        return manifest
            .RootElement.GetProperty("views")
            .EnumerateArray()
            .Single(v => v.GetProperty("operation").GetString() == "bookings")
            .GetProperty("id")
            .GetString()!;
    }

    private static async Task<JsonDocument> PostAsync(
        CheckInApiFactory factory,
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

    private static JsonElement? Bookings(JsonDocument answer) =>
        answer.RootElement.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("views", out var views)
        && views.ValueKind == JsonValueKind.Object
        && views.TryGetProperty("bookings", out var bookings)
        && bookings.ValueKind != JsonValueKind.Null
            ? bookings
            : null;

    private static string Show(JsonDocument answer) => answer.RootElement.GetRawText();
}
