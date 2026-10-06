using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// The lifecycle and data-change subscriptions carry the authorization of the data they stream:
/// the operations authorization for the operations view, each <c>[TraxBroadcast]</c> train's own
/// posture otherwise, with detail withheld from subscribers outside the operations view, and a
/// refusal at subscribe time for a subscriber who could receive nothing.
///
/// <para>Enforces <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md")]
[TestFixture]
public class SubscriptionAuthorizationTests
{
    private const string Adr =
        "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md";

    private const string LifecycleQuery =
        "subscription { onTrainCompleted { sequence trainName failureReason hostName output } }";
    private const string DataChangedQuery = "subscription { onDataChanged { domain } }";

    public interface IAdminOnlyTrain;

    public interface IPublicTrain;

    public interface IMembersTrain;

    public interface IUndeclaredTrain;

    private ServiceProvider? _provider;

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _provider = null;
    }

    #region Operations view

    [Test]
    public async Task OperationsGatedByRole_PlayerLifecycleSubscription_IsRefused()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        (await SubscribeAsync(executor, LifecycleQuery, User("Player")))
            .Refused.Should()
            .BeTrue("the operations view needs the operations authorization, per " + Adr);
    }

    [Test]
    public async Task OperationsGatedByRole_PlayerDataChangedSubscription_IsRefused()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        (await SubscribeAsync(executor, DataChangedQuery, User("Player")))
            .Refused.Should()
            .BeTrue();
    }

    [Test]
    public async Task OperationsGatedByRole_AnonymousSubscription_IsRefused()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        (await SubscribeAsync(executor, LifecycleQuery, Anonymous())).Refused.Should().BeTrue();
        (await SubscribeAsync(executor, DataChangedQuery, Anonymous())).Refused.Should().BeTrue();
    }

    [Test]
    public async Task OperationsGatedByRole_Admin_ReceivesEveryTrainWithFullDetail()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, User("Admin"));
        sub.Refused.Should().BeFalse();

        var received = await sub.NextAsync(() =>
            Publish(Event("Some.Unbroadcast.Train", failureReason: "db at 10.0.0.5 refused"))
        );

        received["trainName"].Should().Be("Some.Unbroadcast.Train");
        received["failureReason"].Should().Be("db at 10.0.0.5 refused");
        received["hostName"].Should().Be("worker-1");
        received["sequence"].Should().Be(1L, "the first event a subscription delivers is number 1");
    }

    [Test]
    public async Task EventsPublishedBeforeASubscription_AreNotReportedAsLostToIt()
    {
        // The sequence numbering is docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md:
        // a subscription starts counting from what was already published when it subscribed.
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        var hook = _provider!.GetRequiredService<GraphQLSubscriptionHook>();
        for (var i = 0; i < 5; i++)
            await hook.OnCompleted(Completed("Some.Earlier.Train"), default);

        await using var sub = await SubscribeAsync(executor, LifecycleQuery, User("Admin"));
        var received = await sub.NextAsync(() =>
            hook.OnCompleted(Completed("Some.Later.Train"), default).GetAwaiter().GetResult()
        );

        received["trainName"].Should().Be("Some.Later.Train");
        received["sequence"]
            .Should()
            .Be(
                1L,
                "the five earlier events were published before the subscription existed, so "
                    + "none of them was lost to it"
            );
    }

    /// <summary>
    /// Two <c>GateOperations(roles:)</c> calls each add a requirement, and the subscriber has to
    /// meet both, as two <c>[Authorize(Roles = ...)]</c> attributes combine in ASP.NET Core.
    /// </summary>
    [Test]
    public async Task OperationsGatedByTwoRoleCalls_RequiresBothRoles()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries()
                .GateOperations(roles: "Admin")
                .GateOperations(roles: "Support")
        );

        (await SubscribeAsync(executor, DataChangedQuery, User("Support")))
            .Refused.Should()
            .BeTrue("each GateOperations(roles:) call is a requirement of its own");
        (await SubscribeAsync(executor, DataChangedQuery, User("Admin", "Support")))
            .Refused.Should()
            .BeFalse();
    }

    #endregion

    #region Broadcast view

    [Test]
    public async Task BroadcastTrainGatedByRole_IsNotDeliveredToAPlayer()
    {
        var executor = await BuildAsync(
            g => g,
            Broadcast<IAdminOnlyTrain>(roles: "Admin"),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, User("Player"));
        sub.Refused.Should().BeFalse();

        // The gated train's event goes first; the first thing the player receives must be the
        // public one.
        var received = await sub.NextAsync(() =>
        {
            Publish(Event(typeof(IAdminOnlyTrain).FullName!));
            Publish(Event(typeof(IPublicTrain).FullName!));
        });

        received["trainName"]
            .Should()
            .Be(
                typeof(IPublicTrain).FullName,
                "a broadcast train is streamed only to subscribers its posture admits, per " + Adr
            );
    }

    [Test]
    public async Task BroadcastTrainGatedByRole_IsDeliveredToAnAdmin()
    {
        var executor = await BuildAsync(g => g, Broadcast<IAdminOnlyTrain>(roles: "Admin"));
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, User("Admin"));

        var received = await sub.NextAsync(() => Publish(Event(typeof(IAdminOnlyTrain).FullName!)));

        received["trainName"].Should().Be(typeof(IAdminOnlyTrain).FullName);
    }

    [Test]
    public async Task BroadcastTrainGatedByRole_IsNotDeliveredToARoleDifferingOnlyInCase()
    {
        // Roles match exactly, as IsInRole compares them (Trax.Docs
        // adr/0026-train-roles-match-exactly-like-authorize.md).
        var executor = await BuildAsync(
            g => g,
            Broadcast<IAdminOnlyTrain>(roles: "Admin"),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, User("admin"));

        var received = await sub.NextAsync(() =>
        {
            Publish(Event(typeof(IAdminOnlyTrain).FullName!));
            Publish(Event(typeof(IPublicTrain).FullName!));
        });

        received["trainName"]
            .Should()
            .Be(
                typeof(IPublicTrain).FullName,
                "a role differing only in case does not satisfy the train, per " + Adr
            );
    }

    [Test]
    public async Task OnlyAuthorizedBroadcastTrains_AnonymousSubscription_IsRefused()
    {
        var executor = await BuildAsync(g => g, Broadcast<IMembersTrain>());

        (await SubscribeAsync(executor, LifecycleQuery, Anonymous()))
            .Refused.Should()
            .BeTrue("a subscriber who could receive nothing is refused, per " + Adr);
    }

    [Test]
    public async Task PublicBroadcastTrain_AnonymousSubscriber_SeesItWithoutInternalDetail()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, Anonymous());

        var received = await sub.NextAsync(() =>
            Publish(Event(typeof(IPublicTrain).FullName!, failureReason: "db at 10.0.0.5 refused"))
        );

        received["failureReason"]
            .Should()
            .Be(
                LifecycleSubscriptionAccess.MaskedFailureReason,
                "outside the operations view a failure reason is masked unless the train raised it "
                    + "for clients, per "
                    + Adr
            );
        received["hostName"].Should().BeNull();
    }

    [Test]
    public async Task PublicBroadcastTrain_TrainExceptionReason_IsShown()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, Anonymous());

        var received = await sub.NextAsync(() =>
            Publish(
                Event(
                    typeof(IPublicTrain).FullName!,
                    failureReason: "Card declined.",
                    failureException: "TrainException"
                )
            )
        );

        received["failureReason"].Should().Be("Card declined.");
    }

    [Test]
    public async Task ObjectOutput_IsDeliveredAsJson()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, LifecycleQuery, Anonymous());

        var received = await sub.NextAsync(() =>
            Publish(Event(typeof(IPublicTrain).FullName!, output: """{"total":3,"items":["a"]}"""))
        );

        var output = received["output"]
            .Should()
            .BeAssignableTo<IReadOnlyDictionary<string, object?>>()
            .Subject;
        output["total"]!.ToString().Should().Be("3");
    }

    [Test]
    public async Task DataChanged_WithoutOperations_RequiresAnAuthenticatedSubscriber()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));

        (await SubscribeAsync(executor, DataChangedQuery, Anonymous())).Refused.Should().BeTrue();
        await using var member = await SubscribeAsync(executor, DataChangedQuery, User("Player"));
        member.Refused.Should().BeFalse();
    }

    [Test]
    public async Task BroadcastTrainWithoutPosture_OnAnOpenEndpoint_FailsAtStartup()
    {
        var act = async () => await BuildAsync(g => g, Undeclared<IUndeclaredTrain>());

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*[TraxBroadcast] train*" + typeof(IUndeclaredTrain).FullName + "*");
    }

    #endregion

    #region Every field declares how it is authorized

    [Test]
    public void EveryLifecycleSubscriptionField_DeclaresItsAuthorization()
    {
        var undeclared = typeof(LifecycleSubscriptions)
            .GetMethods(
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly
            )
            .Where(m => m.IsDefined(typeof(HotChocolate.Types.SubscribeAttribute), false))
            .Where(m =>
                !m.IsDefined(typeof(AuthorizedPerSubscriberAttribute), false)
                && !m.IsDefined(typeof(TraxAuthorizeAttribute), false)
                && !m.IsDefined(typeof(TraxAllowAnonymousAttribute), false)
            )
            .Select(m => m.Name)
            .ToList();

        undeclared
            .Should()
            .BeEmpty(
                "every field on Trax's subscription root declares its authorization, per " + Adr
            );
    }

    [Test]
    public async Task ExposureCensus_AcceptsTraxsOwnSubscriptionFields()
    {
        // A ConfigureSchema callback is what switches the census on; building the schema is the
        // census. Trax's own fields must pass it.
        var act = async () =>
        {
            var discovery = Substitute.For<ITrainDiscoveryService>();
            discovery.DiscoverTrains().Returns([]);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TraxMarker>();
            services.AddSingleton(discovery);
            services.AddSingleton(Substitute.For<IEffectRegistry>());
            services.AddDbContext<OrderTestDbContext>(o =>
                o.UseInMemoryDatabase("SubscriptionCensus_" + Guid.NewGuid())
            );
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g => g.AddDbContext<OrderTestDbContext>().ConfigureSchema(_ => { })
            );
            _provider = services.BuildServiceProvider();
            foreach (
                var hosted in _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            )
                if (hosted.GetType().Name == "TypeExtensionExposureValidator")
                    await hosted.StartAsync(default);
        };

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Helpers

    private static ClaimsPrincipal User(params string[] roles) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, string.Join("+", roles)),
                    .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
                ],
                "Test"
            )
        );

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static TrainRegistration Broadcast<T>(string? roles = null, bool anonymous = false) =>
        Registration(typeof(T), hasAuthorize: !anonymous, anonymous, roles is null ? [] : [roles]);

    private static TrainRegistration Undeclared<T>() =>
        Registration(typeof(T), hasAuthorize: false, anonymous: false, []);

    private static TrainRegistration Registration(
        Type type,
        bool hasAuthorize,
        bool anonymous,
        IReadOnlyList<string> roles
    ) =>
        new()
        {
            ServiceType = type,
            ImplementationType = type,
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = type.FullName!,
            ImplementationTypeName = type.FullName!,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [],
            RequiredRoles = roles,
            HasAuthorizeAttribute = hasAuthorize,
            HasAllowAnonymousAttribute = anonymous,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = true,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private static Metadata Completed(string trainName)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        return metadata;
    }

    private static TrainLifecycleEvent Event(
        string trainName,
        string? failureReason = null,
        string? failureException = null,
        string? output = null
    ) =>
        new(
            MetadataId: 1,
            ExternalId: "ext",
            TrainName: trainName,
            TrainState: TrainState.Completed,
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: failureReason,
            Output: output,
            HostName: "worker-1",
            HostEnvironment: "Production"
        )
        {
            FailureException = failureException,
        };

    private void Publish(TrainLifecycleEvent e) =>
        _provider!
            .GetRequiredService<ITopicEventSender>()
            .SendAsync(nameof(LifecycleSubscriptions.OnTrainCompleted), e)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private async Task<IRequestExecutor> BuildAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure,
        params TrainRegistration[] trains
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        // A query root the broadcast-only hosts need; its fields are irrelevant here.
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("SubscriptionAuthorization_" + Guid.NewGuid())
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
        public async Task<IReadOnlyDictionary<string, object?>> NextAsync(Action publish)
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

            // Publish until the subscriber picks one up: ExecuteAsync can return before the topic
            // subscription is registered, so a single publish can race ahead of it.
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
            return (IReadOnlyDictionary<string, object?>)payload.DataMap()["onTrainCompleted"]!;
        }

        public async ValueTask DisposeAsync()
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    #endregion
}
