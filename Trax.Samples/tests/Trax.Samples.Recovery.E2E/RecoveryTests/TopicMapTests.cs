using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The topic map: three signals run in parallel branches over the seeded corpus, the join writes
/// the pairs, and a crashed branch fails the run, naming the branch, before anything is written.
/// </summary>
[TestFixture]
public class TopicMapTests : RecoveryTestFixture
{
    private static readonly string[] Branches = ["embedding", "cocitation", "authors"];

    [Test]
    public async Task Seeding_Twice_AddsNoWorks()
    {
        // The host seeded the corpus when it started; seeding again finds every work there.
        var added = await CorpusSeeder.SeedAsync(SharedRecoverySetup.Factory.Services);

        added.Should().Be(0);
        await using var db = await TopicMapDb();
        var ids = CorpusFixture.Works.Select(w => w.Id).ToList();
        (await db.Works.CountAsync(w => ids.Contains(w.Id))).Should().Be(CorpusFixture.Works.Count);
    }

    [Test]
    public async Task Run_OverTheWholeCorpus_MapsTopicsAndFindsTheHiddenTwins()
    {
        var input = Input();

        var map = await RunTrain(input);

        map.Papers.Should().Be(CorpusFixture.Works.Count);
        map.CoCitationTrack.Should().Be("Trusted", "most papers cite a work another one cites");
        map.HiddenTwins.Select(t => (t.WorkA, t.WorkB))
            .Should()
            .BeEquivalentTo([("W30009", "W30028"), ("W30010", "W30019")]);
        map.HiddenTwins.Should().OnlyContain(t => t.SharedReferences == 0);
        map.TopicPairs.Should().Be(32);
        map.Strongest.Should().HaveCount(5);
        map.Strongest.Should().BeInDescendingOrder(l => l.Score);
        map.Strongest[0].Should().Match<TopicLink>(l => l.WorkA == "W30011" && l.WorkB == "W30013");

        // The join wrote exactly the pairs the map counts, and nothing else did.
        await using var db = await TopicMapDb();
        var written = await db.TopicPairs.Where(p => p.RunId == input.RunId).ToListAsync();
        written.Should().HaveCount(map.TopicPairs);
        written.Should().OnlyContain(p => p.Score >= 0.3);
    }

    // The demo model trusts shared references in proportion to how much of the slice they link, so
    // the page's three slices take the gate's three tracks.
    [TestCase(2016, "Trusted")]
    [TestCase(2021, "Dampened")]
    [TestCase(2022, "Ignored")]
    public async Task Run_OverASlice_TakesTheCoCitationTrackTheModelChooses(
        int fromYear,
        string track
    )
    {
        var map = await RunTrain(Input(fromYear));

        map.CoCitationTrack.Should().Be(track);
    }

    [Test]
    public async Task CrashedCoCitationBranch_FailsTheRunNamingIt_AndTheJoinWritesNothing()
    {
        var input = Input();
        Faults.Arm(input.RunId, CrashPoint.CoCitation);

        var act = () => RunTrain(input);

        var failure = (await act.Should().ThrowAsync<BranchesFailedException>()).Which;
        failure.Step.Should().Be("Parallel#0");
        failure.Failures.Select(f => f.Branch).Should().Equal("Parallel#0/cocitation");
        failure.Failures[0].Exception.Message.Should().Contain("citation index");
        failure.Message.Should().Contain("Parallel#0/cocitation");

        await using var db = await TopicMapDb();
        (await db.TopicPairs.CountAsync(p => p.RunId == input.RunId))
            .Should()
            .Be(0, "branches compute and only the join writes, and the join never ran");
    }

    [Test]
    public async Task ScheduledRun_CrashedOnce_FailsNamingTheBranch_ThenEveryBranchRecordsItsSteps()
    {
        await Run.StartAsync("TOPIC_MAP", crashOnce: true);

        var first = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(first)).Should().Be("FAILED");

        var failed = await Execution(first);
        failed
            .GetProperty("failureJunction")
            .GetString()
            .Should()
            .StartWith("Parallel#0/cocitation");
        failed
            .GetProperty("failureException")
            .GetString()
            .Should()
            .EndWith(nameof(BranchesFailedException));
        (await PairsWritten(Run.RunId)).Should().Be(0, "the join did not run on attempt 1");

        var second = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");
        (await PairsWritten(Run.RunId)).Should().Be(32);

        // The model answered inside the branch on attempt 1; the retry replayed that answer.
        Decider.Asked(Run.RunId, nameof(SameTopic)).Should().Be(1);

        // Every branch's steps carry ids under the branch: Parallel#0/<branch>/...
        var steps = await Run.TimelineAsync(second);
        var nodeIds = steps
            .Select(s => s.TryGetProperty("nodeId", out var id) ? id.GetString() : null)
            .OfType<string>()
            .ToList();
        foreach (var branch in Branches)
            nodeIds.Should().Contain(id => id.StartsWith($"Parallel#0/{branch}/"));
        nodeIds.Should().Contain("CombineSignals#0").And.Contain("FindHiddenTwins#0");

        // The run graph, read as the page reads it, draws the three branches as tracks of one
        // Parallel node: attempt 1 failed on the gate's Yes track inside cocitation, attempt 2 ran it.
        foreach (var (attempt, state) in new[] { (first, "FAILED"), (second, "COMPLETED") })
            await ExpectGateStepOnTheGraphAsync(attempt, state);
        Stream.Errors.Should().BeEmpty();
    }

    // The attempt's run graph, once it shows the gate's Yes step in the given state.
    private async Task<JsonElement> GraphShowingTheGateStepAsync(long attempt, string state)
    {
        // The step rows trail the run's end by moments: wait for the graph to show the gate's
        // step as it ended rather than read it once.
        JsonElement graph = default;
        (
            await Polling
                .WaitUntilAsync(
                    async () =>
                        string.Equals(
                            GateStepState(graph = await RunGraph(attempt).ConfigureAwait(false)),
                            state,
                            StringComparison.Ordinal
                        ),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromMilliseconds(100)
                )
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"execution {attempt}'s graph should show the gate's Yes step {state}, but "
                ) + $"shows it {GateStepState(graph) ?? "missing"}"
            );

        return graph;
    }

    // The attempt's run graph shows the gate's Yes step in cocitation, in the given state.
    private async Task ExpectGateStepOnTheGraphAsync(long attempt, string state)
    {
        var graph = await GraphShowingTheGateStepAsync(attempt, state).ConfigureAwait(false);

        var parallel = graph
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(n =>
                string.Equals(
                    n.GetProperty("kind").GetString(),
                    "PARALLEL",
                    StringComparison.Ordinal
                )
            );
        var branches = parallel.GetProperty("tracks").EnumerateArray().ToList();
        branches.Select(t => t.GetProperty("name").GetString()).Should().Equal(Branches);

        var gate = branches[1]
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(n =>
                string.Equals(n.GetProperty("kind").GetString(), "GATE", StringComparison.Ordinal)
            );
        gate.GetProperty("trackTaken").GetString().Should().Be("Yes");
        gate.GetProperty("tracks")
            .EnumerateArray()
            .Single(t =>
                string.Equals(t.GetProperty("name").GetString(), "Yes", StringComparison.Ordinal)
            )
            .GetProperty("nodes")[0]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be(state);
    }

    private static TopicMapInput Input(int fromYear = 2016) =>
        new()
        {
            RunId = Guid.NewGuid().ToString("N")[..12],
            Fields = CorpusFixture.Fields,
            FromYear = fromYear,
            ToYear = 2025,
        };

    private static FaultInjector Faults =>
        SharedRecoverySetup.Factory.Services.GetRequiredService<FaultInjector>();

    private static async Task<TopicMap> RunTrain(TopicMapInput input)
    {
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var train = scope.ServiceProvider.GetRequiredService<IBuildTopicMapTrain>();
            return await train.Run(input).ConfigureAwait(false);
        }
    }

    private static Task<TopicMapDbContext> TopicMapDb() =>
        SharedRecoverySetup
            .Factory.Services.GetRequiredService<IDbContextFactory<TopicMapDbContext>>()
            .CreateDbContextAsync();

    private static async Task<int> PairsWritten(string runId)
    {
        var db = await TopicMapDb().ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.TopicPairs.CountAsync(p => p.RunId == runId).ConfigureAwait(false);
        }
    }

    private async Task<JsonElement> Execution(long metadataId)
    {
        var response = await GraphQL
            .SendAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""
                    { operations { executionDetail(id: {{metadataId}}) { failureJunction failureException } } }
                    """
                ),
                OperatorKey
            )
            .ConfigureAwait(false);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("operations", "executionDetail");
    }

    // The state of the step on the co-citation gate's Yes track, or null when the graph has none.
    private static string? GateStepState(JsonElement graph)
    {
        if (graph.ValueKind != JsonValueKind.Object)
            return null;
        var gate = graph
            .GetProperty("nodes")
            .EnumerateArray()
            .Where(n =>
                string.Equals(
                    n.GetProperty("kind").GetString(),
                    "PARALLEL",
                    StringComparison.Ordinal
                )
            )
            .SelectMany(p => p.GetProperty("tracks").EnumerateArray())
            .Where(t =>
                string.Equals(
                    t.GetProperty("name").GetString(),
                    "cocitation",
                    StringComparison.Ordinal
                )
            )
            .SelectMany(t => t.GetProperty("nodes").EnumerateArray())
            .FirstOrDefault(n =>
                string.Equals(n.GetProperty("kind").GetString(), "GATE", StringComparison.Ordinal)
            );
        if (gate.ValueKind != JsonValueKind.Object)
            return null;
        var yes = gate.GetProperty("tracks")
            .EnumerateArray()
            .FirstOrDefault(t =>
                string.Equals(t.GetProperty("name").GetString(), "Yes", StringComparison.Ordinal)
            );
        return
            yes.ValueKind == JsonValueKind.Object
            && yes.TryGetProperty("nodes", out var nodes)
            && nodes.GetArrayLength() > 0
            ? nodes[0].GetProperty("state").GetString()
            : null;
    }

    private async Task<JsonElement> RunGraph(long metadataId)
    {
        var response = await GraphQL
            .SendAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""
                    { operations { runGraph(metadataId: {{metadataId}}) { nodes {
                        id kind state trackTaken tracks { name taken nodes {
                            id kind state trackTaken tracks { name taken nodes {
                                id kind state trackTaken tracks { name taken }
                            } }
                        } }
                    } } } }
                    """
                ),
                OperatorKey
            )
            .ConfigureAwait(false);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("operations", "runGraph");
    }
}
