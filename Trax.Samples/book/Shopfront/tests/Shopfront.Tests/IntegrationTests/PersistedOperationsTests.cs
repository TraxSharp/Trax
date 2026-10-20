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
