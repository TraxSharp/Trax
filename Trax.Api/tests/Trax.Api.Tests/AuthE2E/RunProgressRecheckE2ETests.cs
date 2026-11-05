using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A followed run is asked again at every connection re-check interval, over a real socket, on a host whose endpoint
/// is gated with <c>RequireAuthorization(...)</c>: a stream whose caller no longer satisfies the endpoint policy ends
/// with <c>NOT_FOUND</c>, the answer a refused subscribe gets, and one whose caller still does goes on.
/// <para>Enforces <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md")]
public class RunProgressRecheckE2ETests
{
    private const string Adr =
        "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md";

    private const string Database = "trax_api_run_progress_recheck";
    private const string AliceKey = "recheck-alice-key-0123456789abcdefghijk";
    private const string ActivePolicy = "ActiveAccount";

    private const string EventFields = "sequence state problem { code }";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private IHost _host = null!;
    private volatile bool _active = true;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AliceKey, id: "alice", "Player")
                        );
                        // The endpoint policy, reading state outside the principal.
                        services.AddAuthorization(o =>
                            o.AddPolicy(
                                ActivePolicy,
                                p => p.RequireAuthenticatedUser().RequireAssertion(_ => _active)
                            )
                        );
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJunctionEvents()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        // The endpoint is gated, so only the followed train and one gated query are
                        // discovered: the assembly's [TraxAllowAnonymous] trains would stop such a
                        // host starting.
                        services.AddSingleton<ITrainDiscoveryService>(
                            new OnlyTrains(new TrainDiscoveryService(services))
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL(g =>
                            g.RequireAuthorization(ActivePolicy)
                                .RecheckConnectionCredentialsEvery(Interval)
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseWebSockets();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        ProgressMarkerTrain.Gate.TrySetResult();
        await _host.StopAsync();
        _host.Dispose();
    }

    [SetUp]
    public void Admit()
    {
        _active = true;
        ProgressMarkerTrain.Reset(open: false);
    }

    [TearDown]
    public void OpenGate() => ProgressMarkerTrain.Gate.TrySetResult();

    [Test]
    public async Task A_stream_whose_caller_no_longer_satisfies_the_endpoint_policy_ends_not_found()
    {
        var running = RunAsync();
        var runId = await RunningIdAsync();

        await using var alice = await RunProgressE2ETests.Socket.ConnectAsync(_host, AliceKey);
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        (await alice.NextAsync()).GetProperty("state").GetString().Should().Be("IN_PROGRESS");

        _active = false;

        var events = await alice.ReadUntilCompleteAsync();
        events.Should().NotBeEmpty();
        var last = events[^1];
        last.GetProperty("problem")
            .ValueKind.Should()
            .Be(
                JsonValueKind.Object,
                "the endpoint policy is asked again at the re-check, per "
                    + Adr
                    + ": "
                    + last.GetRawText()
            );
        last.GetProperty("problem").GetProperty("code").GetString().Should().Be("NOT_FOUND");
        last.GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);

        _active = true;
        ProgressMarkerTrain.Gate.TrySetResult();
        await running;
    }

    [Test]
    public async Task A_stream_whose_caller_still_satisfies_the_endpoint_policy_goes_on()
    {
        var running = RunAsync();
        var runId = await RunningIdAsync();

        await using var alice = await RunProgressE2ETests.Socket.ConnectAsync(_host, AliceKey);
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        (await alice.NextAsync()).GetProperty("state").GetString().Should().Be("IN_PROGRESS");

        // allowed-delay: lets three re-checks pass on the real clock the socket runs on; the
        // run's end after them is what the test waits for.
        await Task.Delay(Interval * 3);
        ProgressMarkerTrain.Gate.TrySetResult();
        await running;

        var events = await alice.ReadUntilCompleteAsync();
        events
            .Should()
            .NotContain(
                e => e.GetProperty("problem").ValueKind != JsonValueKind.Null,
                "a caller the endpoint policy still admits keeps receiving"
            );
        events.Last().GetProperty("state").GetString().Should().Be("COMPLETED");
    }

    private async Task RunAsync()
    {
        using var response = await _host.PostGraphQLAsync(
            """mutation { dispatch { progressMarker(input: { note: "recheck", fail: false }, mode: RUN) { externalId } } }""",
            r => r.Headers.Add("X-Api-Key", AliceKey)
        );
        response
            .RootElement.GetProperty("data")
            .ValueKind.Should()
            .NotBe(JsonValueKind.Null, response.RootElement.GetRawText());
    }

    // The external id of the run waiting at the gate, read from its row.
    private async Task<string> RunningIdAsync()
    {
        await ProgressMarkerTrain.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.Name == typeof(IProgressMarkerTrain).FullName
                && m.TrainState == TrainState.InProgress
            )
            .OrderByDescending(m => m.Id)
            .Select(m => m.ExternalId)
            .FirstAsync();
    }

    private sealed class OnlyTrains(ITrainDiscoveryService all) : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() =>
            all.DiscoverTrains()
                .Where(t =>
                    t.ServiceType == typeof(IProgressMarkerTrain)
                    // A query, so the schema has a query root.
                    || t.ServiceType == typeof(IAdminLookupTrain)
                )
                .ToList();
    }
}
