using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>The guarded (committed-state) autosave path and the concurrency guarantees around it.</summary>
public class OrderAutosaveTests
{
    private static SnapshotDraftService<OrderState, OrderTrigger> Service() =>
        TestOrder.Service(TestDb.NewStore());

    private static string PlacedJson(string receipt = "r1") =>
        TestOrder.Machine.Serialize(
            new Snapshot
            {
                Machine = "order",
                Version = 1,
                State = "Placed",
                Context = new JsonObject { ["items"] = new JsonArray(1), ["receipt"] = receipt },
            }
        );

    private async Task<Guid> SeedPlaced(string user)
    {
        var id = Guid.NewGuid();
        (await Service().Autosave(user, id, PlacedJson()))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    [Test]
    public async Task A_stale_autosave_cannot_resurrect_a_committed_draft()
    {
        var id = await SeedPlaced("u");

        (await Service().Autosave("u", id, TestOrder.ReviewJson(1, 2)))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("draft-committed");

        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Placed");
    }

    [Test]
    public async Task A_burst_of_stale_autosaves_over_a_committed_draft_are_all_refused()
    {
        var id = await SeedPlaced("u");

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(_ =>
                    Task.Run(() => Service().Autosave("u", id, TestOrder.ReviewJson(1, 2)))
                )
        );

        results.Should().OnlyContain(r => r is AutosaveResult.Rejected);
        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Placed");
    }

    [Test]
    public async Task A_reset_to_the_initial_state_is_allowed_over_a_committed_draft()
    {
        var id = await SeedPlaced("u");

        (await Service().Autosave("u", id, TestOrder.DraftJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Draft");
    }

    [Test]
    public async Task Twelve_parallel_advances_produce_exactly_one_winner()
    {
        var id = Guid.NewGuid();
        // Seed a Review with items and race an always-valid transition (Back -> Draft): exactly one CAS wins.
        await Service().Autosave("u", id, TestOrder.ReviewJson(1, 2));

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => Task.Run(() => Service().Advance("u", id, "Back")))
        );

        outcomes.Count(o => o is AdvanceOutcome.Advanced).Should().Be(1);
        outcomes
            .Should()
            .OnlyContain(o =>
                o is AdvanceOutcome.Advanced
                || o is AdvanceOutcome.Conflict
                || o is AdvanceOutcome.Rejected
            );
    }

    [Test]
    public async Task Ten_concurrent_creates_of_one_id_are_typed_never_a_primary_key_throw()
    {
        var id = Guid.NewGuid();

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(_ => Task.Run(() => Service().Autosave("u", id, TestOrder.DraftJson)))
        );

        results.Count(r => r is AutosaveResult.Saved).Should().BeGreaterThanOrEqualTo(1);
        results
            .Should()
            .OnlyContain(r => r is AutosaveResult.Saved || r is AutosaveResult.Conflict);
        (await Service().Load("u", id)).Should().BeOfType<LoadResult.Loaded>();
    }
}
