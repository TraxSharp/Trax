using System.Net;
using Trax.Samples.SignalRBroadcaster.E2E.Factories;
using Trax.Samples.SignalRBroadcaster.E2E.Fixtures;

namespace Trax.Samples.SignalRBroadcaster.E2E.HubTests;

/// <summary>
/// The hub sends every train's events to every client it admits, so it admits signed-in operators
/// only, and the only way to sign in is a demo endpoint that exists in Development alone.
/// </summary>
[TestFixture]
public class HubPostureTests
{
    [Test]
    public async Task An_unauthenticated_client_is_refused()
    {
        await using var client = HubClient.Create(SharedHostSetup.Factory, cookie: null);

        var act = () => client.Connection.StartAsync();

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_signed_in_operator_connects()
    {
        var cookie = await HubClient.SignIn(SharedHostSetup.Factory);
        await using var client = HubClient.Create(SharedHostSetup.Factory, cookie);

        await client.Connection.StartAsync();

        client
            .Connection.State.Should()
            .Be(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected);
    }

    [Test]
    public async Task An_anonymous_caller_cannot_run_a_ping()
    {
        using var http = SharedHostSetup.Factory.CreateClient();

        var response = await http.PostAsJsonAsync("/pings", new { outcome = "Succeed" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Outside_development_there_is_no_way_to_sign_in()
    {
        await using var production = new BroadcasterFactory("Production");
        using var http = production.CreateClient();

        var response = await http.PostAsync("/demo/sign-in", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
