using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.Runs;
using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.ChainVerification;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A run's steps, end to end: a real train on a host that called <c>AddJunctionEvents()</c>,
/// followed over a real WebSocket through <c>onJunctionEvent</c> and read back through
/// <c>operations.junctionRuns</c>, by callers with and without the right to see the run.
/// </summary>
/// <remarks>
/// The trains' input, intermediate output, final output and failure message all carry
/// <see cref="Marker"/>; it must appear in nothing any caller receives. One question is about a
/// <c>[TraxSensitive]</c> type, whose answer must stay absent everywhere.
///
/// <para>Enforces <c>docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md</c>.</para>
/// </remarks>
[Property("adr", "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md")]
[TestFixture]
[NonParallelizable]
public class JunctionEventsE2ETests
{
    private const string Adr =
        "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md";
    private const string Database = "trax_api_junction_events";
    private const string WsUri = "ws://localhost/trax/graphql";

    private const string AdminKey = "junction-admin-key-0123456789abcdefghijk";
    private const string PlayerKey = "junction-player-key-0123456789abcdefghij";
    private const string GuestKey = "junction-guest-key-0123456789abcdefghijk";

    /// <summary>Carried by every input, output and failure message the trains handle.</summary>
    internal const string Marker = "S3CRET-MARKER-7f2c";

    private const string StepFields =
        "position kind name state startedAt endedAt durationMs failureClass failureException "
        + "questionKey answer confidence replayed decider answerWithheld attempt nameWithheld "
        + "trackPosition";

    private static string Subscription(long metadataId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"subscription {{ onJunctionEvent(metadataId: {metadataId}) {{ sequence metadataId trainName eventType junction {{ {StepFields} }} }} }}"
        );

    private static string TimelineQuery(long metadataId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ operations {{ junctionRuns(metadataId: {metadataId}) {{ {StepFields} }} }} }}"
        );

    private static string ExecutionQuery(long metadataId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ operations {{ executionDetail(id: {metadataId}) {{ id }} }} }}"
        );

    private IHost? _host;

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
                            keys.Add(AdminKey, id: "admin", "Admin", "Player")
                                .Add(PlayerKey, id: "player", "Player")
                                .Add(GuestKey, id: "guest", "Guest")
                        );
                        // The scanned trains include one gated by AdminPolicy, which must be registered.
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );

                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJunctionEvents()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Admin")
                        );

                        // The operations surface needs the scheduler's service at startup; nothing
                        // read here goes through it.
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
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

        await _host.NotNull().StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await _host.NotNull().StopAsync();
        _host?.Dispose();
    }

    #region Live steps

    [Test]
    public async Task Admin_FollowsARunsSteps_WithoutItsDataAndWithTheSensitiveAnswerWithheld()
    {
        var admin = await Subscriber.ConnectAsync(_host.NotNull(), AdminKey);
        await using var adminDisposal = admin.ConfigureAwait(false);
        var run = await StartRunAsync<IHiddenMarkerTrain>(fail: true);
        await using var runDisposal = run.ConfigureAwait(false);
        await admin.SubscribeAsync(Subscription(run.MetadataId));
        await admin.WaitUntilListeningAsync(() =>
            ProbeAsync(run.MetadataId, typeof(IHiddenMarkerTrain))
        );

        run.Release();
        var steps = await admin.ReadUntilAsync(IsLastStep);
        await run.Finished;

        var events = steps.Select(s => (Type: s.EventType, s.Name)).ToList();
        events
            .Should()
            .ContainInOrder(
                ("JUNCTION_COMPLETED", nameof(HoldForRelease)),
                ("DECIDED", "MarkerLane"),
                ("ROUTED", "MarkerLane"),
                ("DECIDED", "MarkerTier"),
                ("ROUTED", "MarkerTier"),
                ("JUNCTION_STARTED", Withheld),
                ("JUNCTION_FAILED", Withheld)
            );

        var lane = steps.First(s =>
            string.Equals(s.EventType, "DECIDED", StringComparison.Ordinal)
            && string.Equals(s.Name, "MarkerLane", StringComparison.Ordinal)
        );
        lane.Fields.GetProperty("answer").GetString().Should().Be("Fast");
        lane.Fields.GetProperty("confidence").GetDouble().Should().Be(0.8);
        lane.Fields.GetProperty("decider")
            .GetString()
            .Should()
            .Be(typeof(ScriptedDecider).FullName);

        foreach (
            var tier in steps.Where(s =>
                string.Equals(s.Name, "MarkerTier", StringComparison.Ordinal)
            )
        )
        {
            tier.Fields.GetProperty("answerWithheld").GetBoolean().Should().BeTrue();
            tier.Fields.GetProperty("answer")
                .ValueKind.Should()
                .Be(JsonValueKind.Null, "a [TraxSensitive] question's answer is never published");
            tier.Fields.GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var failed = steps[^1];
        failed
            .Fields.GetProperty("failureException")
            .GetString()
            .Should()
            .Be("InvalidOperationException");
        failed.Fields.GetProperty("failureClass").ValueKind.Should().NotBe(JsonValueKind.Null);

        admin
            .Received.Should()
            .NotContain(Marker, "a step carries no input, output or message, per " + Adr);
        admin.Received.Should().NotContain("\"Red\"", "the withheld answer appears nowhere");
        admin
            .Received.Should()
            .NotContain(nameof(RevealSecret), "a junction on a sensitive track is named to nobody")
            .And.NotContain(nameof(FinishOrExplode));
        steps
            .Should()
            .Contain(
                s => s.Name == nameof(PassLane),
                "the operations view sees the names on a track whose answer it may see"
            );
    }

    [Test]
    public async Task Player_ReceivesNoneOfTheStepsOfARunWhoseTrainItCannotSee()
    {
        var admin = await Subscriber.ConnectAsync(_host.NotNull(), AdminKey);
        await using var adminDisposal = admin.ConfigureAwait(false);
        var player = await Subscriber.ConnectAsync(_host.NotNull(), PlayerKey);
        await using var playerDisposal = player.ConfigureAwait(false);
        var run = await StartRunAsync<IHiddenMarkerTrain>(fail: false);
        await using var runDisposal = run.ConfigureAwait(false);

        await admin.SubscribeAsync(Subscription(run.MetadataId));
        await player.SubscribeAsync(Subscription(run.MetadataId));
        // The probes name a train the player may see, so both are known to be listening.
        await admin.WaitUntilListeningAsync(() =>
            ProbeAsync(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );
        await player.WaitUntilListeningAsync(() =>
            ProbeAsync(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );

        run.Release();
        await admin.ReadUntilAsync(IsLastStep);
        await run.Finished;

        // Published after every real step, on the same topic: the player reads it next only if
        // it was handed none of the hidden train's steps.
        await ProbeAsync(run.MetadataId, typeof(IBroadcastMarkerTrain), name: "barrier");
        var next = await player.NextStepAsync();
        while (string.Equals(next.Name, "probe", StringComparison.Ordinal))
            next = await player.NextStepAsync();
        next.Name.Should()
            .Be(
                "barrier",
                "a subscriber who could not see the train's events sees none of its steps, per "
                    + Adr
            );
    }

    [Test]
    public async Task Player_FollowsABroadcastRun_WithHostDetailWithheld()
    {
        var player = await Subscriber.ConnectAsync(_host.NotNull(), PlayerKey);
        await using var playerDisposal = player.ConfigureAwait(false);
        var run = await StartRunAsync<IBroadcastMarkerTrain>(fail: true);
        await using var runDisposal = run.ConfigureAwait(false);
        await player.SubscribeAsync(Subscription(run.MetadataId));
        await player.WaitUntilListeningAsync(() =>
            ProbeAsync(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );

        run.Release();
        var steps = await player.ReadUntilAsync(IsLastStep);
        await run.Finished;

        steps.Should().Contain(s => s.Name == "MarkerLane" && s.EventType == "DECIDED");
        steps
            .Should()
            .OnlyContain(s =>
                s.Fields.GetProperty("decider").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("answer").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("confidence").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("failureException").ValueKind == JsonValueKind.Null
            );
        player.Received.Should().NotContain(Marker);
        player
            .Received.Should()
            .NotContain(
                nameof(PassLane),
                "a junction on a track gives the answer away to a subscriber not shown it, per "
                    + Adr
            );
        steps
            .Where(s => s.Fields.GetProperty("trackPosition").ValueKind == JsonValueKind.Number)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(s =>
                s.Name == Withheld && s.Fields.GetProperty("nameWithheld").GetBoolean()
            );
        player.Received.Should().NotContain("\"Red\"");
    }

    [Test]
    public async Task Guest_WhoCouldSeeNoTrain_IsRefused()
    {
        var guest = await Subscriber.ConnectAsync(_host.NotNull(), GuestKey);
        await using var guestDisposal = guest.ConfigureAwait(false);

        (await guest.SubscribeRefusedAsync(Subscription(1)))
            .Should()
            .BeTrue(
                "a subscriber who could receive nothing is refused when subscribing, per " + Adr
            );
    }

    #endregion

    #region The recorded timeline

    [Test]
    public async Task Timeline_IsReadableExactlyWhenTheRunsDetailIs()
    {
        var run = await StartRunAsync<IHiddenMarkerTrain>(fail: true);
        await using var runDisposal = run.ConfigureAwait(false);
        run.Release();
        await run.Finished;
        var timeline = await TimelineWhenCompleteAsync(run.MetadataId);
        timeline.Should().NotBeEmpty();

        foreach (var key in new[] { AdminKey, PlayerKey, GuestKey, null })
        {
            using var execution = await PostAsync(ExecutionQuery(run.MetadataId), key);
            using var steps = await PostAsync(TimelineQuery(run.MetadataId), key);

            Denied(steps)
                .Should()
                .Be(
                    Denied(execution),
                    $"the timeline is denied exactly when the run's detail is (caller {key ?? "anonymous"}), per {Adr}"
                );
            if (Denied(steps))
                steps.RootElement.GetRawText().Should().NotContain(nameof(HoldForRelease));
        }

        using var forAdmin = await PostAsync(TimelineQuery(run.MetadataId), AdminKey);
        Denied(forAdmin).Should().BeFalse();
    }

    [Test]
    public async Task Timeline_CarriesNoRunDataAndWithholdsTheSensitiveAnswer()
    {
        var run = await StartRunAsync<IBroadcastMarkerTrain>(fail: true);
        await using var runDisposal = run.ConfigureAwait(false);
        run.Release();
        await run.Finished;
        var timeline = await TimelineWhenCompleteAsync(run.MetadataId);

        timeline
            .Select(s => (s.GetProperty("kind").GetString(), s.GetProperty("name").GetString()))
            .Should()
            .Equal(
                ("JUNCTION", nameof(HoldForRelease)),
                ("CHOICE", "MarkerLane"),
                ("ROUTE", "MarkerLane"),
                ("JUNCTION", nameof(PassLane)),
                ("CHOICE", "MarkerTier"),
                ("ROUTE", "MarkerTier"),
                ("JUNCTION", Withheld),
                ("JUNCTION", Withheld)
            );
        timeline[^1].GetProperty("nameWithheld").GetBoolean().Should().BeTrue();
        timeline[^1].GetProperty("trackPosition").GetInt32().Should().Be(5);

        foreach (
            var tier in timeline.Where(s =>
                string.Equals(
                    s.GetProperty("name").GetString(),
                    "MarkerTier",
                    StringComparison.Ordinal
                )
            )
        )
        {
            tier.GetProperty("answerWithheld").GetBoolean().Should().BeTrue();
            tier.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
            tier.GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
        }

        timeline[1].GetProperty("answer").GetString().Should().Be("Fast");
        timeline[^1].GetProperty("state").GetString().Should().Be("FAILED");
        timeline[^1].GetProperty("durationMs").ValueKind.Should().Be(JsonValueKind.Number);

        var raw = string.Join("", timeline.Select(s => s.GetRawText()));
        raw.Should()
            .NotContain(Marker, "a recorded step carries no input, output or message, per " + Adr);
        raw.Should().NotContain("\"Red\"");
    }

    [Test]
    public async Task Timeline_PagesByPosition()
    {
        var run = await StartRunAsync<IHiddenMarkerTrain>(fail: false);
        await using var runDisposal = run.ConfigureAwait(false);
        run.Release();
        await run.Finished;
        await TimelineWhenCompleteAsync(run.MetadataId);

        using var doc = await PostAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{{ operations {{ junctionRuns(metadataId: {run.MetadataId}, afterPosition: 2, take: 3) {{ position }} }} }}"
            ),
            AdminKey
        );

        doc.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("junctionRuns")
            .EnumerateArray()
            .Select(s => s.GetProperty("position").GetInt32())
            .Should()
            .Equal(3, 4, 5);
    }

    [TestCase(0L)]
    [TestCase(-5L)]
    public async Task Timeline_OfAnIdThatIsNotPositive_IsRefused(long metadataId)
    {
        using var doc = await PostAsync(TimelineQuery(metadataId), AdminKey);

        AuthOperations
            .HasErrorCode(doc, Trax.Api.GraphQL.Validation.RunIdArgument.ErrorCode)
            .Should()
            .BeTrue(
                "an id of 0 would read every unsaved run at once, per "
                    + Adr
                    + ": "
                    + doc.RootElement.GetRawText()
            );
    }

    [Test]
    public async Task Timeline_OverGet_IsRefusedLikeEveryOtherQuery()
    {
        var client = _host.NotNull().GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/trax/graphql?query=" + Uri.EscapeDataString(TimelineQuery(1))
        );
        request.Headers.Add("X-Api-Key", AdminKey);
        request.Headers.Add("GraphQL-preflight", "1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("junctionRuns\":");
    }

    #endregion

    #region The declared graph

    private const string RunNodeFields = "id kind state replayed trackTaken steps { position }";

    private static string RunGraphQuery(long metadataId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ operations {{ runGraph(metadataId: {metadataId}) {{ metadataId train hasGraph hash moreSteps "
        )
        + $"unmatchedSteps {{ position nodeId }} nodes {{ {RunNodeFields} tracks {{ name taken nodes {{ {RunNodeFields} }} }} }} }} }} }}";

    [Test]
    public async Task TheDeclaredGraph_OfANameNoTrainIsRegisteredUnder_IsNull()
    {
        var graphs = _host.NotNull().Services.GetRequiredService<ITrainChainGraphs>();
        foreach (
            var name in new[]
            {
                "System.String",
                typeof(MarkerTrainBase).FullName.NotNull(),
                "Nope",
            }
        )
            graphs.Find(name).Should().BeNull(name);
    }

    [Test]
    public async Task RunGraph_PlacesEveryRecordedStep_OnItsNodeOrAsUnmatched()
    {
        var run = await StartRunAsync<IHiddenMarkerTrain>(fail: true);
        await using var runDisposal = run.ConfigureAwait(false);
        run.Release();
        await run.Finished;
        var timeline = await TimelineWhenCompleteAsync(run.MetadataId);

        using var doc = await PostAsync(RunGraphQuery(run.MetadataId), AdminKey);
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ValueKind == JsonValueKind.Undefined ? "" : errors.GetRawText());
        var graph = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("runGraph");

        graph.GetProperty("hasGraph").GetBoolean().Should().BeTrue();
        graph.GetProperty("train").GetString().Should().Be(typeof(IHiddenMarkerTrain).FullName);
        graph.GetProperty("hash").GetString().Should().HaveLength(64);
        var nodes = graph.GetProperty("nodes");
        nodes
            .EnumerateArray()
            .Select(n => n.GetProperty("id").GetString())
            .Should()
            .Equal(
                "Seed<IDecider>#0",
                nameof(HoldForRelease) + "#0",
                "Decide<ChoiceDecision<MarkerLane>>#0",
                "Switch<MarkerLane>#0",
                "Decide<ChoiceDecision<MarkerTier>>#0",
                "Switch<MarkerTier>#0",
                nameof(FinishOrExplode) + "#0",
                "Resolve#0"
            );
        nodes[3]
            .GetProperty("tracks")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .Should()
            .Equal("Fast", "Slow");
        var byId = nodes
            .EnumerateArray()
            .ToDictionary(n => n.GetProperty("id").GetString().NotNull(), StringComparer.Ordinal);
        byId["Seed<IDecider>#0"].GetProperty("state").GetString().Should().Be("NOT_RECORDED");
        byId[nameof(HoldForRelease) + "#0"]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("COMPLETED");
        byId["Decide<ChoiceDecision<MarkerLane>>#0"]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("COMPLETED");

        var lane = byId["Switch<MarkerLane>#0"];
        lane.GetProperty("trackTaken").GetString().Should().Be(nameof(MarkerLane.Fast));
        var fast = lane.GetProperty("tracks")[0];
        fast.GetProperty("taken").GetBoolean().Should().BeTrue();
        fast.GetProperty("nodes")[0].GetProperty("state").GetString().Should().Be("COMPLETED");
        lane.GetProperty("tracks")[1]
            .GetProperty("nodes")[0]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("SKIPPED");

        // The sensitive route's track is not named, and nor is anything after it: each node there
        // says its steps are withheld rather than that the run never reached it.
        var tier = byId["Switch<MarkerTier>#0"];
        tier.GetProperty("trackTaken").ValueKind.Should().Be(JsonValueKind.Null);
        tier.GetProperty("tracks")
            .EnumerateArray()
            .SelectMany(t => t.GetProperty("nodes").EnumerateArray())
            .Select(n => n.GetProperty("state").GetString())
            .Should()
            .OnlyContain(s => s == "WITHHELD");
        byId[nameof(FinishOrExplode) + "#0"]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("WITHHELD");

        // None is lost: every recorded step is on a node or listed unmatched.
        var unmatched = graph.GetProperty("unmatchedSteps").EnumerateArray().ToList();
        Positions(nodes)
            .Concat(unmatched.Select(s => s.GetProperty("position").GetInt32()))
            .Should()
            .BeEquivalentTo(timeline.Select(s => s.GetProperty("position").GetInt32()));

        // The steps after the sensitive route carry no node id, which would name its track.
        unmatched
            .Should()
            .OnlyContain(s => s.GetProperty("nodeId").ValueKind == JsonValueKind.Null);

        foreach (var key in new[] { PlayerKey, GuestKey, null })
        {
            using var refused = await PostAsync(RunGraphQuery(run.MetadataId), key);
            Denied(refused)
                .Should()
                .BeTrue($"the graph names the train's types (caller {key ?? "anonymous"})");
            refused.RootElement.GetRawText().Should().NotContain(nameof(HoldForRelease));
        }
    }

    private static IEnumerable<int> Positions(JsonElement nodes) =>
        nodes
            .EnumerateArray()
            .SelectMany(n =>
                n.GetProperty("steps")
                    .EnumerateArray()
                    .Select(s => s.GetProperty("position").GetInt32())
                    .Concat(
                        n.TryGetProperty("tracks", out var tracks)
                            ? tracks
                                .EnumerateArray()
                                .SelectMany(t => Positions(t.GetProperty("nodes")))
                            : []
                    )
            );

    #endregion

    #region Parallel

    private const string Fork = "Parallel#0";
    private const string NestedFork = "Parallel#0/right/Parallel#0";

    // Deep enough for a Parallel inside a branch: step, branch, step, branch, step.
    private static string ParallelRunGraphQuery(long metadataId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{ operations {{ runGraph(metadataId: {metadataId}) {{ unmatchedSteps {{ position }} "
        )
        + $"nodes {{ {ParallelNodeFields} tracks {{ name taken nodes {{ {ParallelNodeFields} "
        + $"tracks {{ name taken nodes {{ {ParallelNodeFields} tracks {{ name }} }} }} }} }} }} }} }} }}";

    private const string ParallelNodeFields = "id kind state trackTaken";

    [Test]
    public void TheDeclaredGraph_OfAParallelTrain_HasTheStep_WithEveryBranchAsATrack()
    {
        var nodes = _host
            .NotNull()
            .Services.GetRequiredService<ITrainChainGraphs>()
            .Find(typeof(IParallelMarkerTrain).FullName.NotNull())
            .NotNull()
            .Nodes;

        var fork = nodes.Single(n => string.Equals(n.Id, Fork, StringComparison.Ordinal));
        fork.Kind.Should().Be(ChainStepKind.Parallel);
        fork.Tracks.Select(b => b.Name).Should().Equal("left", "right");
        fork.Tracks.Should().OnlyContain(b => !b.IsFallback);
        fork.Tracks[0].Nodes.Select(n => n.Id).Should().Equal($"{Fork}/left/{nameof(ScoreLeft)}#0");

        var nested = fork.Tracks[1].Nodes[1];
        nested.Id.Should().Be(NestedFork);
        nested.Kind.Should().Be(ChainStepKind.Parallel);
        nested
            .Tracks.Select(b => b.Nodes[0].Id)
            .Should()
            .Equal($"{NestedFork}/x/{nameof(CountX)}#0", $"{NestedFork}/y/{nameof(CountY)}#0");
    }

    [TestCase(false, "COMPLETED")]
    [TestCase(true, "FAILED")]
    public async Task RunGraph_OfAParallelRun_ShowsEachBranch_AsTheDashboardDoes(
        bool fail,
        string forkState
    )
    {
        var run = await StartRunAsync<IParallelMarkerTrain>(fail);
        await using var runDisposal = run.ConfigureAwait(false);
        run.Release();
        await run.Finished;
        var rows = await StableStepsAsync(run.MetadataId, fail ? 2 : 6);

        using var doc = await PostAsync(ParallelRunGraphQuery(run.MetadataId), AdminKey);
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ValueKind == JsonValueKind.Undefined ? "" : errors.GetRawText());
        var graph = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("runGraph");

        // The dashboard's run page places the steps it reads through RunGraphs.Match, against the
        // graph the host's registry holds: the same read, done its way, must agree node for node.
        var train = typeof(IParallelMarkerTrain).FullName.NotNull();
        var dashboard = RunGraphs.Match(
            run.MetadataId,
            train,
            _host.NotNull().Services.GetRequiredService<ITrainChainGraphs>().Find(train),
            rows
        );
        NodeStates(graph.GetProperty("nodes"))
            .Should()
            .Equal(DashboardStates(dashboard.Nodes), "the API and the dashboard share one read");
        graph.GetProperty("unmatchedSteps").GetArrayLength().Should().Be(0);

        var fork = graph
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(n =>
                string.Equals(n.GetProperty("id").GetString(), Fork, StringComparison.Ordinal)
            );
        fork.GetProperty("state").GetString().Should().Be(forkState);
        fork.GetProperty("trackTaken").ValueKind.Should().Be(JsonValueKind.Null);
        fork.GetProperty("tracks")
            .EnumerateArray()
            .Should()
            .OnlyContain(b => b.GetProperty("taken").GetBoolean(), "every branch runs");

        var states = NodeStates(graph.GetProperty("nodes"))
            .ToDictionary(s => s.Id, s => s.State, StringComparer.Ordinal);
        states[$"{Fork}/left/{nameof(ScoreLeft)}#0"].Should().Be(fail ? "FAILED" : "COMPLETED");
        states[$"{nameof(JoinScores)}#0"].Should().Be(fail ? "NOT_REACHED" : "COMPLETED");
        if (!fail)
            states[NestedFork].Should().Be("COMPLETED");

        // Each recorded step says which branch it ran in, from the column Effect writes.
        rows.Single(r =>
                string.Equals(
                    r.NodeId,
                    $"{Fork}/left/{nameof(ScoreLeft)}#0",
                    StringComparison.Ordinal
                )
            )
            .BranchPath.Should()
            .Be($"{Fork}/left");
        rows.Where(r => r.NodeId?.StartsWith(Fork + "/", StringComparison.Ordinal) != true)
            .Should()
            .OnlyContain(r => r.BranchPath == null, "a step outside the Parallel is in no branch");
    }

    /// <summary>
    /// The run's steps as the dashboard reads them, once the writer has caught up: at least
    /// <paramref name="atLeast"/> of them, none in progress, and the same on two reads in a row.
    /// </summary>
    private async Task<List<JunctionStep>> StableStepsAsync(long metadataId, int atLeast)
    {
        var factory = _host.NotNull().Services.GetRequiredService<IDataContextProviderFactory>();
        var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(15);
        List<JunctionStep>? last = null;
        while (true)
        {
            var context = await factory.CreateDbContextAsync(default).ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            var steps = (
                await context
                    .JunctionRuns.AsNoTracking()
                    .ForRun(metadataId)
                    .ToListAsync()
                    .ConfigureAwait(false)
            )
                .Select(JunctionStep.From)
                .ToList();

            var settled =
                steps.Count >= atLeast
                && steps.TrueForAll(s => s.State != JunctionRunState.InProgress);
            if (
                settled
                && last is not null
                && steps
                    .Select(s => (s.Position, s.State))
                    .SequenceEqual(last.Select(s => (s.Position, s.State)))
            )
                return steps;
            last = settled ? steps : null;

            if (TimeProvider.System.GetUtcNow().UtcDateTime > deadline)
                Assert.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The steps of run {metadataId} were not recorded in time."
                    )
                );

            // allowed-delay: polls the background writer, bounded by the 15s deadline above.
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private static IEnumerable<(string Id, string State)> NodeStates(JsonElement nodes) =>
        nodes
            .EnumerateArray()
            .SelectMany(n =>
                new[]
                {
                    (
                        n.GetProperty("id").GetString().NotNull(),
                        n.GetProperty("state").GetString().NotNull()
                    ),
                }.Concat(
                    n.GetProperty("tracks")
                        .EnumerateArray()
                        .SelectMany(t => NodeStates(t.GetProperty("nodes")))
                )
            );

    // The dashboard writes a node's state as the enum's name; the schema as its GraphQL value.
    private static IEnumerable<(string Id, string State)> DashboardStates(
        IEnumerable<RunGraphNode> nodes
    ) =>
        nodes.SelectMany(n =>
            new[] { (n.Id, GraphQLName(n.State)) }.Concat(
                n.Tracks.SelectMany(t => DashboardStates(t.Nodes))
            )
        );

    private static string GraphQLName(RunNodeState state) =>
        string.Concat(
                state.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{c}" : $"{c}")
            )
            .ToUpperInvariant();

    #endregion

    #region Helpers

    private const string Withheld = JunctionStep.WithheldName;

    /// <summary>The run's last junction (position 7) ending; its name is withheld on every view.</summary>
    private static bool IsLastStep(Step step) =>
        step.Fields.GetProperty("position").GetInt32() == 7
        && step.EventType is "JUNCTION_FAILED" or "JUNCTION_COMPLETED";

    private static bool Denied(JsonDocument doc) =>
        AuthOperations.HasErrorCode(doc, "TRAX_AUTHORIZATION")
        || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHORIZED")
        || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHENTICATED");

    private async Task<List<JsonElement>> TimelineWhenCompleteAsync(long metadataId)
    {
        // The rows are written off the run's path and trail it by moments.
        var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(15);
        while (true)
        {
            using var doc = await PostAsync(TimelineQuery(metadataId), AdminKey)
                .ConfigureAwait(false);
            doc.RootElement.TryGetProperty("errors", out var errors)
                .Should()
                .BeFalse(errors.ValueKind == JsonValueKind.Undefined ? "" : errors.GetRawText());
            var steps = doc
                .RootElement.GetProperty("data")
                .GetProperty("operations")
                .GetProperty("junctionRuns")
                .EnumerateArray()
                .Select(s => s.Clone())
                .ToList();

            if (
                steps.Count > 0
                && steps[^1].GetProperty("position").GetInt32() == 7
                && !string.Equals(
                    steps[^1].GetProperty("state").GetString(),
                    "IN_PROGRESS",
                    StringComparison.Ordinal
                )
            )
                return steps;

            if (TimeProvider.System.GetUtcNow().UtcDateTime > deadline)
                Assert.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The timeline of run {metadataId} was not recorded in time."
                    )
                );

            // allowed-delay: polls the background writer, bounded by the 15s deadline above.
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private async Task<JsonDocument> PostAsync(string query, string? apiKey)
    {
        var client = _host.NotNull().GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await client.SendAsync(request).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Publishes a made-up step of <paramref name="metadataId"/> through the host's own junction
    /// event handler, as the run's path does.
    /// </summary>
    private async Task ProbeAsync(long metadataId, Type train, string name = "probe")
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: metadataId,
            ExternalId: "probe",
            TrainName: train.FullName.NotNull(),
            TrainState: nameof(TrainState.InProgress),
            Timestamp: TimeProvider.System.GetUtcNow().UtcDateTime,
            FailureJunction: null,
            FailureReason: null,
            EventType: TrainLifecycleEventMessage.JunctionStartedEventType,
            Executor: null,
            Output: null
        )
        {
            Junction = new JunctionEventPayload(
                Position: 999,
                Kind: JunctionRunKind.Junction,
                Name: name,
                State: JunctionRunState.InProgress,
                StartedAt: TimeProvider.System.GetUtcNow().UtcDateTime
            ),
        };

        foreach (var handler in _host.NotNull().Services.GetServices<IJunctionEventHandler>())
            await handler.HandleAsync(message, default).ConfigureAwait(false);
    }

    private async Task<RunningTrain> StartRunAsync<TTrain>(bool fail)
        where TTrain : IServiceTrain<MarkerInput, string>
    {
        var key = Guid.NewGuid().ToString("N");
        var gate = MarkerGates.Hold(key);
        var scope = _host.NotNull().Services.CreateAsyncScope();
        var train = scope.ServiceProvider.GetRequiredService<TTrain>();
        var finished = Task.Run(async () =>
        {
            try
            {
                await train.Run(new MarkerInput(key, Marker, fail)).ConfigureAwait(false);
            }
            catch (Exception) when (fail)
            {
                // The failing run fails by design.
            }
        });

        var concrete = (ServiceTrain<MarkerInput, string>)(object)train;
        var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(15);
        while (concrete.Metadata is not { Id: > 0 })
        {
            if (TimeProvider.System.GetUtcNow().UtcDateTime > deadline || finished.IsCompleted)
            {
                gate.TrySetResult();
                await finished.ConfigureAwait(false);
                Assert.Fail("The run did not start in time.");
            }

            // allowed-delay: waits for the run to persist its metadata, bounded by the deadline.
            await Task.Delay(20, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        return new RunningTrain(scope, gate, concrete.Metadata.Id, finished);
    }

    private sealed class RunningTrain(
        IServiceScope scope,
        TaskCompletionSource gate,
        long metadataId,
        Task finished
    ) : IAsyncDisposable
    {
        public long MetadataId => metadataId;

        public Task Finished => finished.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System);

        public void Release() => gate.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            gate.TrySetResult();
            await finished.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            scope.Dispose();
        }
    }

    internal sealed record Step(string EventType, string Name, JsonElement Fields);

    /// <summary>One graphql-transport-ws connection with one subscription.</summary>
    private sealed class Subscriber : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly StringBuilder _received = new();

        // Real steps that arrived while waiting for the first probe: the run starts before the
        // subscription, so on a slow machine its first junction can reach the socket before a probe.
        private readonly Queue<Step> _early = new();

        private Subscriber(WebSocket socket) => _socket = socket;

        /// <summary>Every frame received, raw, for checks that nothing leaked.</summary>
        public string Received => _received.ToString();

        public static async Task<Subscriber> ConnectAsync(IHost host, string apiKey)
        {
            var client = host.GetTestServer().CreateWebSocketClient();
            client.SubProtocols.Add("graphql-transport-ws");
            var socket = await client
                .ConnectClosingAsync(new Uri(WsUri), CancellationToken.None)
                .ConfigureAwait(false);
            var subscriber = new Subscriber(socket);
            await subscriber
                .SendAsync(new { type = "connection_init", payload = new { apiKey } })
                .ConfigureAwait(false);
            var ack = await subscriber.ReceiveAsync().ConfigureAwait(false);
            ack.GetProperty("type").GetString().Should().Be("connection_ack");
            return subscriber;
        }

        /// <summary>
        /// Starts the subscription. An accepted subscription answers nothing until an event, so
        /// <see cref="WaitUntilListeningAsync"/> is what shows it was accepted: a refusal arrives
        /// there instead of a probe and fails the read.
        /// </summary>
        public Task SubscribeAsync(string query) =>
            SendAsync(
                new
                {
                    id = "1",
                    type = "subscribe",
                    payload = new { query },
                }
            );

        /// <summary>
        /// Starts a subscription expected to be refused, and whether it was: an <c>error</c> frame,
        /// or a result carrying errors and no data. False when nothing arrives before the read
        /// times out, which is how an accepted subscription with no events answers.
        /// </summary>
        public async Task<bool> SubscribeRefusedAsync(string query)
        {
            await SubscribeAsync(query).ConfigureAwait(false);

            JsonElement message;
            try
            {
                message = await ReceiveAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            return string.Equals(
                    message.GetProperty("type").GetString(),
                    "error",
                    StringComparison.Ordinal
                ) || message.GetProperty("payload").TryGetProperty("errors", out _);
        }

        /// <summary>
        /// Publishes probes until this subscriber receives one. A real step that arrives first is
        /// kept for <see cref="ReadUntilAsync"/>.
        /// </summary>
        public async Task WaitUntilListeningAsync(Func<Task> probe)
        {
            var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(10);
            var next = ReceiveStepAsync();
            while (TimeProvider.System.GetUtcNow().UtcDateTime < deadline)
            {
                if (next.IsCompleted)
                {
                    var step = await next.ConfigureAwait(false);
                    if (string.Equals(step.Name, "probe", StringComparison.Ordinal))
                        return;
                    _early.Enqueue(step);
                    next = ReceiveStepAsync();
                    continue;
                }

                await probe().ConfigureAwait(false);
                // allowed-delay: re-probe interval, bounded by the deadline; WhenAny wakes on receipt.
                await Task.WhenAny(next, Task.Delay(100)).ConfigureAwait(false);
            }

            next.IsCompleted.Should().BeTrue("the subscription should start listening");
            (await next.ConfigureAwait(false)).Name.Should().Be("probe");
        }

        /// <summary>Reads real steps, skipping probes, until <paramref name="last"/> matches.</summary>
        public async Task<List<Step>> ReadUntilAsync(Func<Step, bool> last)
        {
            var steps = new List<Step>();
            while (true)
            {
                var step = await NextStepAsync().ConfigureAwait(false);
                if (string.Equals(step.Name, "probe", StringComparison.Ordinal))
                    continue;

                steps.Add(step);
                if (last(step))
                    return steps;
            }
        }

        /// <summary>The next step: one kept while waiting for the first probe, else the next to arrive.</summary>
        public Task<Step> NextStepAsync() =>
            _early.Count > 0 ? Task.FromResult(_early.Dequeue()) : ReceiveStepAsync();

        private async Task<Step> ReceiveStepAsync()
        {
            while (true)
            {
                var message = await ReceiveAsync().ConfigureAwait(false);
                var type = message.GetProperty("type").GetString();
                if (type is "ping" or "pong")
                    continue;

                type.Should().Be("next", message.GetRawText());
                var payload = message.GetProperty("payload");
                payload.TryGetProperty("errors", out _).Should().BeFalse(payload.GetRawText());
                var e = payload.GetProperty("data").GetProperty("onJunctionEvent");
                var step = e.GetProperty("junction");
                return new Step(
                    e.GetProperty("eventType").GetString().NotNull(),
                    step.GetProperty("name").GetString().NotNull(),
                    step.Clone()
                );
            }
        }

        private async Task SendAsync(object message)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _socket
                .SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cts.Token)
                .ConfigureAwait(false);
        }

        private async Task<JsonElement> ReceiveAsync()
        {
            var buffer = new byte[8192];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException(
                        $"The socket closed: {result.CloseStatus} {result.CloseStatusDescription}"
                    );

                await ms.WriteAsync(buffer, 0, result.Count, CancellationToken.None)
                    .ConfigureAwait(false);
                if (result.EndOfMessage && ms.Length > 0)
                    break;
            }

            var text = Encoding.UTF8.GetString(ms.ToArray());
            lock (_received)
                _received.Append(text).Append('\n');
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
