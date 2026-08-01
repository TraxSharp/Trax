using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Lazy on-read draft expiry: a draft idle past the configured TTL is deleted and reported as
/// <see cref="LoadResult.NotFound"/> on the next <c>Load</c> (so the user starts fresh). Staleness is forced
/// deterministically by backdating <c>updated_at</c>, the analogue of the effect-claim tests' negative lease.
/// </summary>
public class SnapshotDraftExpiryTests
{
    private static SnapshotDraftService<TurnstileState, TurnstileTrigger> Turnstile(
        TimeSpan? ttl
    ) => new(TestTurnstile.Machine, TestDb.NewStore(), draftTtl: ttl);

    private static SnapshotDraftService<OrderState, OrderTrigger> Order(TimeSpan? ttl) =>
        new(
            TestOrder.Machine,
            TestDb.NewStore(),
            committedStates: new[] { OrderState.Placed },
            draftTtl: ttl
        );

    [Test]
    public async Task Load_deletes_a_draft_idle_past_the_ttl_and_reports_not_found()
    {
        var id = Guid.NewGuid();
        (await Turnstile(TimeSpan.FromMinutes(1)).Autosave("u", id, TestTurnstile.UnlockedJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        await TestDb.BackdateDraft("u", id, DateTimeOffset.UtcNow.AddHours(-1));

        (await Turnstile(TimeSpan.FromMinutes(1)).Load("u", id))
            .Should()
            .BeOfType<LoadResult.NotFound>();
        (await TestDb.NewStore().Get("u", id))
            .Should()
            .BeNull("the expired row is removed, not just ignored");
    }

    [Test]
    public async Task Load_keeps_a_draft_within_the_ttl()
    {
        var id = Guid.NewGuid();
        await Turnstile(TimeSpan.FromMinutes(30)).Autosave("u", id, TestTurnstile.UnlockedJson);

        (await Turnstile(TimeSpan.FromMinutes(30)).Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
        (await TestDb.NewStore().Get("u", id)).Should().NotBeNull();
    }

    [Test]
    public async Task Load_with_no_ttl_never_expires_a_draft()
    {
        var id = Guid.NewGuid();
        await Turnstile(null).Autosave("u", id, TestTurnstile.UnlockedJson);
        await TestDb.BackdateDraft("u", id, DateTimeOffset.UtcNow.AddYears(-1));

        (await Turnstile(null).Load("u", id)).Should().BeOfType<LoadResult.Loaded>();
    }

    [Test]
    public async Task Load_expires_a_committed_draft_the_same_as_any_other()
    {
        var id = Guid.NewGuid();
        // A committed (Placed) draft — the kind a soft autosave can't overwrite. Expiry must still clear it,
        // so a returning user isn't wedged behind their own completed order.
        (await Order(TimeSpan.FromMinutes(1)).Autosave("u", id, TestOrder.PlacedJson("r1", 1, 2)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        await TestDb.BackdateDraft("u", id, DateTimeOffset.UtcNow.AddHours(-1));

        (await Order(TimeSpan.FromMinutes(1)).Load("u", id))
            .Should()
            .BeOfType<LoadResult.NotFound>();
        (await TestDb.NewStore().Get("u", id)).Should().BeNull();
    }
}
