using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// The lifecycle subscription fields take an optional <c>externalId</c>, so a client that queued a
/// run follows that run alone instead of filtering every broadcast event itself. Authorization is
/// unchanged: the filter narrows what the subscriber's visibility already admits.
/// </summary>
[TestFixture]
public class LifecycleExternalIdFilterTests
{
    private static readonly string[] LifecycleFields =
    [
        "onTrainStarted",
        "onTrainCompleted",
        "onTrainFailed",
        "onTrainCancelled",
        "onTrainStateChanged",
    ];

    private ServiceProvider? _provider;

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _provider = null;
    }

    [Test]
    public async Task EveryLifecycleField_TakesAnOptionalExternalId()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        var subscription = executor.Schema.SubscriptionType!;

        foreach (var name in LifecycleFields)
        {
            var field = subscription.Fields[name];
            field.Arguments.Should().ContainSingle(a => a.Name == "externalId", name);
            var argument = field.Arguments["externalId"];
            argument.Type.IsNonNullType().Should().BeFalse(name + "'s externalId is optional");
            argument.Type.NamedType().Name.Should().Be("String");
        }
    }

    [Test]
    public async Task SubscriptionWithExternalId_ReceivesOnlyThatRun()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(
            executor,
            """subscription { onTrainCompleted(externalId: "run-b") { externalId sequence } }""",
            User("Admin")
        );

        var received = await sub.NextAsync(
            () =>
            {
                Publish(nameof(LifecycleSubscriptions.OnTrainCompleted), Event("run-a"));
                Publish(nameof(LifecycleSubscriptions.OnTrainCompleted), Event("run-b"));
            },
            "onTrainCompleted"
        );

        received["externalId"].Should().Be("run-b");
    }

    [Test]
    public async Task StateChangedWithExternalId_IgnoresOtherRuns()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(
            executor,
            """subscription { onTrainStateChanged(externalId: "run-b") { externalId } }""",
            User("Admin")
        );

        // Other runs are published many times first: none of them may be the one delivered.
        for (var i = 0; i < 20; i++)
            Publish(nameof(LifecycleSubscriptions.OnTrainStateChanged), Event("run-a"));
        var received = await sub.NextAsync(
            () => Publish(nameof(LifecycleSubscriptions.OnTrainStateChanged), Event("run-b")),
            "onTrainStateChanged"
        );

        received["externalId"].Should().Be("run-b");
    }

    [Test]
    public async Task SubscriptionWithoutExternalId_ReceivesEveryRun()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(
            executor,
            "subscription { onTrainCompleted { externalId } }",
            User("Admin")
        );

        var received = await sub.NextAsync(
            () => Publish(nameof(LifecycleSubscriptions.OnTrainCompleted), Event("run-a")),
            "onTrainCompleted"
        );

        received["externalId"].Should().Be("run-a");
    }

    [Test]
    public async Task ExternalId_DoesNotWidenAuthorization()
    {
        // No broadcast train and an operations gate the subscriber fails: refused whatever the
        // filter names.
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        var sub = await SubscribeAsync(
            executor,
            """subscription { onTrainCompleted(externalId: "run-b") { externalId } }""",
            User("Player")
        );

        sub.Refused.Should().BeTrue();
    }

    #region Helpers

    private static ClaimsPrincipal User(params string[] roles) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "user"),
                    .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
                ],
                "Test"
            )
        );

    private static TrainLifecycleEvent Event(string externalId) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "Some.Train",
            TrainState: TrainState.Completed,
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            Output: null
        );

    private void Publish(string topic, TrainLifecycleEvent e) =>
        _provider!
            .GetRequiredService<ITopicEventSender>()
            .SendAsync(topic, e)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private async Task<IRequestExecutor> BuildAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("LifecycleExternalId_" + Guid.NewGuid())
        );
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            g => configure(g.AddDbContext<OrderTestDbContext>())
        );

        _provider = services.BuildServiceProvider();
        return await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static async Task<Subscription> SubscribeAsync(
        IRequestExecutor executor,
        string query,
        ClaimsPrincipal user
    )
    {
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(query)
                .SetGlobalState("ClaimsPrincipal", user)
                .Build()
        );

        return result is IResponseStream stream ? new Subscription(stream) : new Subscription(null);
    }

    private sealed class Subscription(IResponseStream? stream) : IAsyncDisposable
    {
        public bool Refused => stream is null;

        /// <summary>
        /// Publishes with <paramref name="publish"/> until the subscriber receives something, and
        /// returns the first payload it receives.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, object?>> NextAsync(
            Action publish,
            string field
        )
        {
            stream.Should().NotBeNull();
            var received = new TaskCompletionSource<OperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var reader = Task.Run(async () =>
            {
                await foreach (var item in stream!.ReadResultsAsync())
                {
                    received.TrySetResult(item);
                    break;
                }
            });

            // ExecuteAsync can return before the topic subscription is registered, so a single
            // publish can race ahead of it.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!received.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                publish();
                // allowed-delay: re-publish interval, bounded by the 10s deadline; WhenAny wakes
                // the instant the subscriber receives.
                await Task.WhenAny(received.Task, Task.Delay(100));
            }

            received.Task.IsCompleted.Should().BeTrue("an event should reach the subscriber");
            var payload = await received.Task;
            payload.Errors.Should().BeNullOrEmpty();
            await reader;
            return (IReadOnlyDictionary<string, object?>)payload.DataMap()[field]!;
        }

        public async ValueTask DisposeAsync()
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    #endregion
}
