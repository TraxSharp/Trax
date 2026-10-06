using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// Decision recording against Sqlite: each decision is a <c>trax.decision</c> row written while
/// the run is still going, deleted with its run, and read back by a requeued run that names it in
/// <c>replay_decisions_of</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SqliteDecisionRecordingTests
{
    private ServiceProvider _provider = null!;

    private static readonly LiteDecider Decider = new();

    private string _dbPath = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_decisions_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(Decider);
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UseSqlite($"Data Source={_dbPath}").AddDecisionRecording()
            )
        );
        services
            .AddScopedTraxRoute<ILiteRouteOrder, LiteRouteOrder>()
            .AddScopedTraxRoute<ILiteRouteThenPeek, LiteRouteThenPeek>()
            .AddScopedTraxRoute<ILiteUnanswered, LiteUnanswered>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider()
    {
        await _provider.DisposeAsync();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(path))
                File.Delete(path);
    }

    [Test]
    public async Task A_decision_is_a_row_written_while_the_run_is_in_progress()
    {
        Decider.Choice = LiteFulfilment.ManualCheck;

        var (train, output) = await Run<ILiteRouteThenPeek>();

        output.Should().Be("held");
        LitePeek.Seen.Should().Equal((train.Metadata!.Id, TrainState.InProgress, "ManualCheck"));

        var row = (await Recorded(train.Metadata.Id)).Should().ContainSingle().Subject;
        row.QuestionKey.Should().Be(QuestionKey.For<LiteFulfilment>());
        row.Occurrence.Should().Be(0);
        row.Fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        row.StateHash.Should()
            .MatchRegex("^s1:[0-9a-f]{64}$", "the hash of the state is stored with the answer");
        row.Kind.Should().Be("choice");
        row.Answer.Should().Contain("\"choice\":\"ManualCheck\"");
        row.Replayed.Should().BeFalse();
        row.Tracks().Should().Equal("ManualCheck");
        (await RunRow(train.Metadata.Id)).DecisionsRecorded.Should().BeTrue();

        await Delete(train.Metadata.Id);
    }

    [Test]
    public async Task A_runs_decisions_are_deleted_with_it()
    {
        Decider.Choice = LiteFulfilment.Standard;
        var (train, _) = await Run<ILiteRouteOrder>();
        (await Recorded(train.Metadata!.Id)).Should().ContainSingle();

        await Delete(train.Metadata.Id);

        (await Recorded(train.Metadata.Id)).Should().BeEmpty("the foreign key cascades");
    }

    [Test]
    public async Task A_requeued_run_replays_the_decisions_of_the_run_it_names()
    {
        Decider.Choice = LiteFulfilment.ManualCheck;
        var (original, first) = await Run<ILiteRouteOrder>();
        first.Should().Be("held");

        Decider.Choice = LiteFulfilment.Standard;
        Decider.Asked = 0;
        var (requeued, output) = await Run<ILiteRouteOrder>(
            replayDecisionsOf: original.Metadata!.Id
        );

        output.Should().Be("held");
        Decider.Asked.Should().Be(0);
        var replayed = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        replayed.Replayed.Should().BeTrue();
        replayed.Tracks().Should().Equal("ManualCheck");

        await Delete(original.Metadata.Id, requeued.Metadata.Id);
    }

    [Test]
    public async Task A_requeue_of_a_deleted_run_fails_as_permanent_without_asking()
    {
        Decider.Choice = LiteFulfilment.ManualCheck;
        var (original, _) = await Run<ILiteRouteOrder>();
        await Delete(original.Metadata!.Id);
        Decider.Asked = 0;

        var run = async () => await Run<ILiteRouteOrder>(replayDecisionsOf: original.Metadata.Id);

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain($"no run {original.Metadata.Id} exists");
        Decider.Asked.Should().Be(0);
        _lastTrain!.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);

        await Delete(_lastTrain.Metadata.Id);
    }

    [Test]
    public async Task A_refused_answer_is_a_row_with_why_and_its_requeue_asks_afresh()
    {
        Decider.Raw = "Banana";
        var refuse = async () => await Run<ILiteRouteOrder>();

        (await refuse.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("'Banana', which is not one of its options");
        var original = _lastTrain!;
        original.Metadata!.FailureClass.Should().Be(FailureClass.Transient);

        var refused = (await Recorded(original.Metadata.Id)).Should().ContainSingle().Subject;
        refused.QuestionKey.Should().Be(QuestionKey.For<LiteFulfilment>());
        refused.Answer.Should().Contain("Banana");
        refused.Refused.Should().Contain("'Banana', which is not one of its options");
        refused.Tracks().Should().BeEmpty();

        Decider.Raw = null;
        Decider.Choice = LiteFulfilment.ManualCheck;
        Decider.Asked = 0;
        var (requeued, output) = await Run<ILiteRouteOrder>(
            replayDecisionsOf: original.Metadata.Id
        );

        output.Should().Be("held");
        Decider.Asked.Should().Be(1, "a refused answer is never replayed");
        var asked = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        asked.Replayed.Should().BeFalse();
        asked.Refused.Should().BeNull();
        asked.Answer.Should().NotContain("replay_refused");

        await Delete(original.Metadata.Id, requeued.Metadata.Id);
    }

    [Test]
    public async Task A_missing_answer_is_a_refused_row_with_no_answer()
    {
        Decider.Raw = null;
        var refuse = async () => await Run<ILiteUnanswered>();

        await refuse.Should().ThrowAsync<TrainException>();
        var row = (await Recorded(_lastTrain!.Metadata!.Id)).Should().ContainSingle().Subject;
        row.Answer.Should().BeNull();
        row.Refused.Should().Contain("gave no answer");

        await Delete(_lastTrain.Metadata.Id);
    }

    private IServiceTrain<LiteOrder, string>? _lastTrain;

    private async Task<(IServiceTrain<LiteOrder, string> Train, string Output)> Run<TTrain>(
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<LiteOrder, string>
    {
        using var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<LiteOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        _lastTrain = train;
        var order = new LiteOrder("lite");

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = order,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        return (train, await train.Run(order, metadata));
    }

    private async Task<List<Models.RecordedDecision.RecordedDecision>> Recorded(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .ToListAsync();
    }

    private async Task<Metadata> RunRow(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == metadataId);
    }

    private async Task Delete(params long[] metadataIds)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        await context.Metadatas.Where(m => metadataIds.Contains(m.Id)).ExecuteDeleteAsync();
    }

    public sealed class LiteDecider : IDecider
    {
        public LiteFulfilment Choice { get; set; }

        /// <summary>A choice to answer with in place of <see cref="Choice"/>, fitting or not.</summary>
        public string? Raw { get; set; }

        public int Asked { get; set; }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            Asked++;

            return Task.FromResult(
                new DecisionResult(
                    new Dictionary<string, Answer>
                    {
                        [QuestionKey.For<LiteFulfilment>()] = new ChoiceAnswer(
                            Raw ?? Choice.ToString()
                        ),
                    }
                )
            );
        }
    }

    public sealed record LiteOrder(string Id);

    [Asks("How should this order be fulfilled?")]
    public enum LiteFulfilment
    {
        Standard,
        ManualCheck,
    }

    public interface ILiteRouteOrder : IServiceTrain<LiteOrder, string>;

    public class LiteRouteOrder : ServiceTrain<LiteOrder, string>, ILiteRouteOrder
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Switch<LiteOrder, LiteFulfilment>(tracks =>
                    tracks
                        .When(LiteFulfilment.Standard, t => t.Chain<LiteShip>())
                        .When(LiteFulfilment.ManualCheck, t => t.Chain<LiteHold>())
                )
                .Resolve();
    }

    [Asks("How soon must this order ship?")]
    public enum LitePriority
    {
        Normal,
        Rush,
    }

    /// <summary>Asks a question the decider never answers.</summary>
    public interface ILiteUnanswered : IServiceTrain<LiteOrder, string>;

    public class LiteUnanswered : ServiceTrain<LiteOrder, string>, ILiteUnanswered
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Switch<LiteOrder, LitePriority>(tracks =>
                    tracks
                        .When(LitePriority.Normal, t => t.Chain<LiteShip>())
                        .When(LitePriority.Rush, t => t.Chain<LiteHold>())
                )
                .Resolve();
    }

    public interface ILiteRouteThenPeek : IServiceTrain<LiteOrder, string>;

    public class LiteRouteThenPeek : ServiceTrain<LiteOrder, string>, ILiteRouteThenPeek
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Switch<LiteOrder, LiteFulfilment>(tracks =>
                    tracks
                        .When(LiteFulfilment.Standard, t => t.Chain<LiteShip>())
                        .When(LiteFulfilment.ManualCheck, t => t.Chain<LiteHold>())
                )
                .Chain<LitePeek>()
                .Resolve();
    }

    public class LiteShip : Junction<LiteOrder, string>
    {
        public override Task<string> Run(LiteOrder input) => Task.FromResult("shipped");
    }

    public class LiteHold : Junction<LiteOrder, string>
    {
        public override Task<string> Run(LiteOrder input) => Task.FromResult("held");
    }

    /// <summary>Reads the run's decisions on a connection of its own, as another process would.</summary>
    public class LitePeek(IDataContextProviderFactory factory) : EffectJunction<string, string>
    {
        public static List<(long, TrainState, string?)> Seen { get; } = [];

        public override async Task<string> Run(string input)
        {
            Seen.Clear();
            var id = Metadata!.TrainMetadataId;
            using var context = await factory.CreateDbContextAsync(CancellationToken.None);

            var state = await context
                .Metadatas.AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => m.TrainState)
                .SingleAsync();

            foreach (
                var row in await context
                    .RecordedDecisions.AsNoTracking()
                    .Where(d => d.MetadataId == id)
                    .ToListAsync()
            )
                Seen.Add((row.MetadataId, state, row.Tracks().SingleOrDefault()));

            return input;
        }
    }
}

/// <summary>Reads the tracks out of a recorded decision's routes.</summary>
internal static class RecordedRoutes
{
    public static IReadOnlyList<string> Tracks(
        this Models.RecordedDecision.RecordedDecision decision
    ) =>
        decision.Routes is null
            ? []
            : System
                .Text.Json.Nodes.JsonNode.Parse(decision.Routes)!
                .AsArray()
                .Select(route => (string)route!["track"]!)
                .ToList();
}
