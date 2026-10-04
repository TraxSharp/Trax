using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// The Api's startup checks refuse a real host before any other hosted service starts, whether the
/// host starts its services one after another or all at once
/// (<c>HostOptions.ServicesStartConcurrently</c>). They check in <c>StartingAsync</c>, which the
/// host finishes for every service before it calls any <c>StartAsync</c>; Kestrel and a host's
/// workers start in <c>StartAsync</c>.
/// <para>Enforces <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-a-misconfigured-host-fails-at-startup.md")]
[TestFixture]
public class StartupGateHostTests
{
    private const string Adr = "docs/adr/0001-a-misconfigured-host-fails-at-startup.md";

    [TestCase(false)]
    [TestCase(true)]
    public async Task ATrainRegisteredAfterAddTraxGraphQL_RefusesTheHostNamingIt_BeforeItsWorkerStarts(
        bool concurrent
    )
    {
        var worker = new MarkerWorker();
        var trains = new List<TrainRegistration> { Train<IEarlyQueryTrain>(isQuery: true) };
        using var host = BuildHost(
            concurrent,
            worker,
            trains,
            graphql => graphql,
            afterGraphQL: () => trains.Add(Train<ILateMutationTrain>(isQuery: false))
        );

        var start = async () => await host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain(typeof(ILateMutationTrain).FullName)
            .And.Contain("before AddTraxGraphQL()");
        worker
            .Started.Should()
            .BeFalse(
                "a check that refuses the host runs before any other service starts, per " + Adr
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnOperationsSurfaceWithoutItsServices_RefusesTheHost_BeforeItsWorkerStarts(
        bool concurrent
    )
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent,
            worker,
            [Train<IEarlyQueryTrain>(isQuery: true)],
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );

        var start = async () => await host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("IOperationsService is not registered");
        worker
            .Started.Should()
            .BeFalse(
                "under ServicesStartConcurrently every StartAsync begins at once, so the check "
                    + "runs in StartingAsync, per "
                    + Adr
            );
    }

    [Test]
    public async Task EveryTrainRegisteredBeforeAddTraxGraphQL_TheHostStarts()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: true,
            worker,
            [Train<IEarlyQueryTrain>(isQuery: true), Train<ILateMutationTrain>(isQuery: false)],
            graphql => graphql
        );

        await host.StartAsync();
        await host.StopAsync();

        worker.Started.Should().BeTrue();
    }

    /// <summary>
    /// A host whose own worker is registered before Trax. <paramref name="trains"/> is what the
    /// discovery service reports when asked, so a train added in <paramref name="afterGraphQL"/>
    /// is one the finished container exposes and <c>AddTraxGraphQL()</c> did not see.
    /// </summary>
    private static IHost BuildHost(
        bool concurrent,
        MarkerWorker worker,
        List<TrainRegistration> trains,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure,
        Action? afterGraphQL = null
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Services.Configure<HostOptions>(o => o.ServicesStartConcurrently = concurrent);
        builder.Services.AddSingleton<IHostedService>(worker);

        builder.Services.AddLogging();
        builder.Services.AddSingleton<TraxMarker>();
        builder.Services.AddSingleton(Substitute.For<IEffectRegistry>());
        builder.Services.AddSingleton<ITrainDiscoveryService>(new ListDiscovery(trains));

        builder.Services.AddTraxGraphQL(configure);
        afterGraphQL?.Invoke();

        return builder.Build();
    }

    private static TrainRegistration Train<TService>(bool isQuery) =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(object),
            InputType = typeof(GateInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TService).Name[1..],
            HasAllowAnonymousAttribute = true,
            InputTypeName = nameof(GateInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    private sealed class ListDiscovery(List<TrainRegistration> trains) : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => trains.ToList();
    }

    private sealed class MarkerWorker : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private interface IEarlyQueryTrain;

    private interface ILateMutationTrain;

    public record GateInput
    {
        public string Value { get; init; } = "";
    }
}
