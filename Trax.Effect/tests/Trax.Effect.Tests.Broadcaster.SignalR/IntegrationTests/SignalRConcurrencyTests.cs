using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Broadcaster.SignalR.Fixtures;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

[TestFixture]
public class SignalRConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static TrainLifecycleEventMessage Message(string externalId, string eventType) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "T.IDoThing",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        );

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Test]
    public async Task ParallelDispatch_AllMessagesDelivered_NoLoss()
    {
        await using var server = await SignalRTestServer.StartAsync();
        await using var connection = server.CreateClient();

        var received = new ConcurrentBag<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => received.Add(evt));

        await connection.StartAsync().WaitAsync(Timeout);

        var dispatcher = server.GetService<SignalRTrainEventDispatcher>();

        var ids = Enumerable.Range(0, 200).Select(i => $"id-{i}").ToArray();
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (id, ct) => await dispatcher.HandleAsync(Message(id, "Completed"), ct)
        );

        var ok = await WaitUntilAsync(() => received.Count >= ids.Length, Timeout);
        ok.Should().BeTrue("all dispatched events should reach the client within the timeout");

        received.Select(e => e.ExternalId).Should().BeEquivalentTo(ids);
    }

    [Test]
    public async Task ParallelDispatch_FilterApplied_OnlyMatchingEventsDelivered()
    {
        await using var server = await SignalRTestServer.StartAsync(opts =>
            opts.OnlyForEvents("Completed")
        );
        await using var connection = server.CreateClient();

        var received = new ConcurrentBag<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => received.Add(evt));

        await connection.StartAsync().WaitAsync(Timeout);

        var dispatcher = server.GetService<SignalRTrainEventDispatcher>();

        var completedIds = Enumerable.Range(0, 50).Select(i => $"c-{i}").ToArray();
        var failedIds = Enumerable.Range(0, 50).Select(i => $"f-{i}").ToArray();

        await Parallel.ForEachAsync(
            completedIds
                .Select(id => (id, type: "Completed"))
                .Concat(failedIds.Select(id => (id, type: "Failed"))),
            async (item, ct) => await dispatcher.HandleAsync(Message(item.id, item.type), ct)
        );

        var ok = await WaitUntilAsync(() => received.Count >= completedIds.Length, Timeout);
        ok.Should().BeTrue();

        // Verifying a negative: that no excluded events ever leak through. Wait for
        // stability — the count is unchanged across two consecutive polls — instead
        // of a fixed duration, so the test fails fast on real machines but still
        // catches a late-arriving leak.
        int prev;
        do
        {
            prev = received.Count;
            await Task.Delay(50);
        } while (received.Count != prev);

        received.Select(e => e.ExternalId).Should().BeEquivalentTo(completedIds);
    }
}
