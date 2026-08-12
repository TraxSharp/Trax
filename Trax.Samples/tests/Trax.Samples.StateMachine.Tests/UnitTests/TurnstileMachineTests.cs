using Trax.Samples.StateMachine.Tests.Fakes;

namespace Trax.Samples.StateMachine.Tests.UnitTests;

/// <summary>
/// Drives the sample <see cref="TurnstileMachine"/> (the pure-structure machine, no effect) through the
/// persistence service over an in-memory store: an accepted coin unlocks and records how it was paid, an
/// unaccepted coin is a typed rejection at the same HTTP path.
/// </summary>
public class TurnstileMachineTests
{
    private static readonly Guid Id = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private const string User = "alice";

    private static async Task<ISnapshotDraftService> LockedTurnstile(InMemorySnapshotStore store)
    {
        var service = new TurnstileMachine().CreateService(store, claims: null);
        (
            await service.Autosave(
                User,
                Id,
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}"
            )
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return service;
    }

    [Test]
    public async Task An_accepted_coin_unlocks_and_records_how_it_was_paid()
    {
        var service = await LockedTurnstile(new InMemorySnapshotStore());

        var snapshot = (
            await service.Advance(User, Id, "Coin", JsonNode.Parse("{\"coin\":\"quarter\"}"))
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot;

        snapshot.State.Should().Be("Unlocked");
        snapshot.Context["paidWith"]!.GetValue<string>().Should().Be("quarter");
    }

    [Test]
    public async Task An_unaccepted_coin_is_rejected_and_the_draft_stays_locked()
    {
        var service = await LockedTurnstile(new InMemorySnapshotStore());

        (await service.Advance(User, Id, "Coin", JsonNode.Parse("{\"coin\":\"penny\"}")))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("guard-failed");

        (await service.Load(User, Id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Locked");
    }
}
