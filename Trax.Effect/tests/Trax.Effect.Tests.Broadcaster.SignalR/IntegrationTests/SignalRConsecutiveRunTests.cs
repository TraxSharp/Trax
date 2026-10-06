using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Tests.Broadcaster.SignalR.Fixtures;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

/// <summary>
/// A run ends by disposing its lifecycle hook runner, which disposes the hooks it was given. The
/// SignalR sink is one dispatcher shared by every run, so a run's end must not stop it.
/// </summary>
[TestFixture]
public class SignalRConsecutiveRunTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static Metadata NewRun(string externalId)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "T.IDoThing",
                ExternalId = externalId,
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        return metadata;
    }

    private static async Task RunOnce(IServiceProvider services, string externalId)
    {
        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ILifecycleHookRunner>();
        try
        {
            await runner.OnCompleted(NewRun(externalId), CancellationToken.None);
        }
        finally
        {
            // What ServiceTrain does when a run ends.
            runner.Dispose();
        }
    }

    [Test]
    public async Task TwoConsecutiveRuns_BothRunsEventsReachTheClient()
    {
        await using var server = await SignalRTestServer.StartAsync(throughAddTrax: true);
        await using var connection = server.CreateClient();

        var received = new ConcurrentQueue<TraxClientEvent>();
        var secondRunArrived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.On<TraxClientEvent>(
            "TrainEvent",
            evt =>
            {
                received.Enqueue(evt);
                if (evt.ExternalId == "run-2" && evt.EventType == "Completed")
                    secondRunArrived.TrySetResult();
            }
        );
        await connection.StartAsync().WaitAsync(Timeout);

        await RunOnce(server.Host.Services, "run-1");
        await RunOnce(server.Host.Services, "run-2");

        await secondRunArrived.Task.WaitAsync(Timeout);
        received.Select(e => e.ExternalId).Should().Contain(["run-1", "run-2"]);
    }
}
