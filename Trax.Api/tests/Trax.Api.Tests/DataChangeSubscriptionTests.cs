using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Builds the real HotChocolate "trax" schema and exercises the <c>onDataChanged</c> subscription
/// end to end. This is what proves the assumption the dashboard bets on: that <c>ChangeDomain</c>
/// serializes to <c>WORK_QUEUE</c>-style names and that publishing a signal actually reaches a
/// subscriber. The mock SDL can't prove either (it's hand-written).
/// </summary>
[TestFixture]
public class DataChangeSubscriptionTests
{
    private ServiceProvider _provider = null!;

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private async Task<IRequestExecutor> BuildExecutorAsync()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());

        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql =>
                graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .AllowAnonymousOperations()
        );

        _provider = services.BuildServiceProvider();
        return await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    [Test]
    public async Task Schema_ExposesOnDataChanged_WithConstantCaseChangeDomainEnum()
    {
        var executor = await BuildExecutorAsync();
        var sdl = executor.Schema.ToString();

        sdl.Should().Contain("onDataChanged: DataChangedEvent!");
        sdl.Should().Contain("type DataChangedEvent");
        sdl.Should().Contain("domain: ChangeDomain!");

        // The dashboard filters on these exact strings. If HotChocolate named the enum differently
        // the client would silently never refetch, so pin every value.
        sdl.Should().Contain("enum ChangeDomain");
        foreach (
            var value in new[]
            {
                "WORK_QUEUE",
                "DEAD_LETTER",
                "MANIFEST",
                "MANIFEST_GROUP",
                "SCHEDULER_CONFIG",
            }
        )
            sdl.Should().Contain(value);
    }

    [Test]
    public async Task OnDataChanged_DeliversPublishedSignal_ToSubscriber()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync(
            "subscription { onDataChanged { domain timestamp } }"
        );
        var stream = result.ExpectResponseStream();

        var received = new TaskCompletionSource<OperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reader = Task.Run(async () =>
        {
            await foreach (var item in stream.ReadResultsAsync())
            {
                received.TrySetResult(item);
                break;
            }
        });

        // Publish until the subscriber picks it up: ExecuteAsync returns before the topic
        // subscription is guaranteed registered, so a single publish can race ahead of it.
        var sender = _provider.GetRequiredService<ITopicEventSender>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!received.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await sender.SendAsync(
                nameof(LifecycleSubscriptions.OnDataChanged),
                new DataChangedEvent(ChangeDomain.WorkQueue, DateTime.UtcNow)
            );
            // allowed-delay: poll/re-publish interval, not a sleep-for-sync. Re-sends because
            // ExecuteAsync can return before the topic subscription is registered; WhenAny wakes
            // the instant the subscriber receives, and the whole loop is bounded by the 10s deadline.
            await Task.WhenAny(received.Task, Task.Delay(100));
        }

        received
            .Task.IsCompleted.Should()
            .BeTrue("the published signal should reach the subscriber");
        var payload = await received.Task;
        payload.Errors.Should().BeNullOrEmpty();

        var data = (IReadOnlyDictionary<string, object?>)payload.DataMap()["onDataChanged"]!;
        data["domain"]!.ToString().Should().Be("WORK_QUEUE");

        await stream.DisposeAsync();
        await reader;
    }
}
