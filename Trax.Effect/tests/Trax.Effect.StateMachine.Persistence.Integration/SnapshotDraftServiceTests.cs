using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>The FE-drives / BE-validates operations over the fast (no committed states) turnstile path.</summary>
public class SnapshotDraftServiceTests
{
    private static SnapshotDraftService<TurnstileState, TurnstileTrigger> Service() =>
        TestTurnstile.Service(TestDb.NewStore());

    [Test]
    public async Task Autosave_validates_then_stores_and_a_load_returns_it()
    {
        var id = Guid.NewGuid();
        (await Service().Autosave("u", id, TestTurnstile.UnlockedJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        (await Service().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task Autosave_rejects_an_invalid_snapshot_and_stores_nothing()
    {
        var id = Guid.NewGuid();
        var invalid =
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{}}"; // missing paidWith

        (await Service().Autosave("u", id, invalid))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.InvalidContext);
        (await Service().Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task Autosave_rejects_an_oversized_payload_before_any_db_work()
    {
        var id = Guid.NewGuid();
        var huge =
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\""
            + new string('x', SnapshotLimits.MaxSnapshotBytes + 1)
            + "\"}}";

        (await Service().Autosave("u", id, huge))
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be("too-large");
        (await Service().Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task Advance_re_drives_from_the_stored_snapshot()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson); // Locked

        var advanced = await Service()
            .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" });
        advanced
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task Advance_declines_an_illegal_trigger_as_a_typed_rejection()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);

        (await Service().Advance("u", id, "Push"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public async Task Advance_on_another_users_draft_is_not_found()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("owner", id, TestTurnstile.InitialJson);

        (await Service().Advance("intruder", id, "Coin", new JsonObject { ["coin"] = "quarter" }))
            .Should()
            .BeOfType<AdvanceOutcome.NotFound>();
    }

    [Test]
    public async Task A_repeated_requestId_replays_while_a_new_one_re_fires()
    {
        var id = Guid.NewGuid();
        await Service().Autosave("u", id, TestTurnstile.InitialJson);

        await Service()
            .Advance("u", id, "Coin", new JsonObject { ["coin"] = "quarter" }, requestId: "r1");

        // Same requestId as the last applied advance -> replay the current state, don't fire Push.
        var replay = await Service().Advance("u", id, "Push", requestId: "r1");
        replay
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");

        // A new requestId actually fires Push.
        var refire = await Service().Advance("u", id, "Push", requestId: "r2");
        refire
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Locked");
    }
}
