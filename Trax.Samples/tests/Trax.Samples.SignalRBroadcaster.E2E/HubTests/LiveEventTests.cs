using System.Net;
using Trax.Samples.SignalRBroadcaster.E2E.Fixtures;

namespace Trax.Samples.SignalRBroadcaster.E2E.HubTests;

/// <summary>
/// Events arrive on the hub as the train runs, in the sample's own projection, which carries a
/// failure reason only when the train meant it for clients.
/// </summary>
[TestFixture]
public class LiveEventTests
{
    private string _cookie = null!;
    private HubClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _cookie = await HubClient.SignIn(SharedHostSetup.Factory);
        _client = HubClient.Create(SharedHostSetup.Factory, _cookie);
        await _client.Connection.StartAsync();
    }

    [TearDown]
    public async Task TearDown() => await _client.DisposeAsync();

    [Test]
    public async Task A_ping_raises_started_and_completed_events_live()
    {
        await RunPing("Succeed");

        var started = await _client.WaitFor(e =>
            e is { TrainName: "IPingTrain", EventType: "Started" }
        );
        var completed = await _client.WaitFor(e =>
            e.EventType == "Completed" && e.ExternalId == started.ExternalId
        );

        completed.FailureReason.Should().BeNull();
    }

    [Test]
    public async Task A_failure_the_train_wrote_for_clients_shows_its_reason()
    {
        await RunPing("FailForClients");

        var failed = await _client.WaitFor(e => e.EventType == "Failed");

        failed.FailureReason.Should().Be("The ping target did not answer.");
    }

    [Test]
    public async Task Any_other_failure_is_masked()
    {
        await RunPing("FailUnexpectedly");

        var failed = await _client.WaitFor(e => e.EventType == "Failed");

        failed.FailureReason.Should().Be(LiveTrainEvent.MaskedReason);
        failed.FailureReason.Should().NotContain("10.0.4.17");
    }

    private async Task RunPing(string outcome)
    {
        using var http = SharedHostSetup.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/pings")
        {
            Content = JsonContent.Create(new { outcome }),
        };
        request.Headers.Add("Cookie", _cookie);

        var response = await http.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
