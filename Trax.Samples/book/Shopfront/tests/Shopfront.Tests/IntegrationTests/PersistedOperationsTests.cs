using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
using Trax.Core.Extensions;
using Trax.StateMachine;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PersistedOperationsTests
{
    private static readonly ClientManifest Manifest = ClientManifest.Read(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json"))
    );

    private static readonly ClientManifestDocument StartCheckout = Manifest
        .Machines.Single(m => string.Equals(m.Id, "checkout", StringComparison.Ordinal))
        .Documents.Single(d => string.Equals(d.Operation, "start", StringComparison.Ordinal));

    private WebApplicationFactory<Program>? _app;

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
    public async ValueTask StopTheApplication()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }

    private HttpClient Ada()
    {
        var client = _app.NotNull().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("ada", Roles.Customer, TimeProvider.System)
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
