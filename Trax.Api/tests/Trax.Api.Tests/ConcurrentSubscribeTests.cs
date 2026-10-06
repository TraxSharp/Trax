using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Subscribers that subscribe to one topic at the same moment each hear the next event on it.
/// HotChocolate's topic loses one of two subscribes that land together; Trax's subscriptions
/// register through <see cref="TopicSubscribe"/>, which takes them one at a time.
/// </summary>
[TestFixture]
public class ConcurrentSubscribeTests
{
    private const string Topic = "concurrent-subscribe";
    private const int Subscribers = 10;

    // Subscribing on HotChocolate's receiver directly loses a subscriber in roughly one trial in four at this width,
    // so a hundred trials without a loss is not luck.
    private const int Trials = 100;

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ConcurrentSubscribers_EachHearTheNextEvent()
    {
        for (var trial = 0; trial < Trials; trial++)
        {
            await using var provider = HostServices().BuildServiceProvider();
            var receiver = provider.GetRequiredService<ITopicEventReceiver>();
            var sender = provider.GetRequiredService<ITopicEventSender>();

            // The topic exists before the race, so every subscribe takes the path that adds to it.
            var first = await receiver.SubscribeAsync<string>(Topic);
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var racing = Enumerable
                .Range(0, Subscribers)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await start.Task;
                        return await TopicSubscribe.OneAtATimeAsync<string>(
                            receiver,
                            Topic,
                            CancellationToken.None
                        );
                    })
                )
                .ToArray();
            start.SetResult();
            ISourceStream<string>[] streams = [first, .. await Task.WhenAll(racing)];

            await sender.SendAsync(Topic, "event");

            using var cts = new CancellationTokenSource(Deadline);
            var heard = await Task.WhenAll(streams.Select(s => FirstEventAsync(s, cts.Token)));
            heard
                .Count(h => h is null)
                .Should()
                .Be(
                    0,
                    "trial {0}: every subscriber was subscribed before the event was sent",
                    trial
                );

            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    private static async Task<string?> FirstEventAsync(
        ISourceStream<string> stream,
        CancellationToken ct
    )
    {
        try
        {
            await foreach (var message in stream.ReadEventsAsync().WithCancellation(ct))
                return message;
        }
        catch (OperationCanceledException) { }
        return null;
    }

    private static IServiceCollection HostServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddSingleton(Substitute.For<IOperationsService>());
        services.AddSingleton(Substitute.For<ITrainExecutionService>());
        services.AddTraxGraphQL(g => g.ExposeOperationQueries().AllowAnonymousOperations());
        return services;
    }
}
