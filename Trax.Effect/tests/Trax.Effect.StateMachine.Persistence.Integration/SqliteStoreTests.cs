using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>The effect-claim and snapshot stores on SQLite, over tables built by the shipped migrations.</summary>
public class SqliteStoreTests
{
    private SqliteDb _db = null!;

    [SetUp]
    public async Task SetUp() => _db = await SqliteDb.Create();

    [TearDown]
    public void TearDown() => _db.Dispose();

    private static string Key() => $"claim:{Guid.NewGuid()}";

    [Test]
    public async Task A_second_active_claim_loses_instead_of_throwing()
    {
        var key = Key();

        (await _db.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>();
        (await _db.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Lost>();
    }

    [Test]
    public async Task An_expired_claim_is_reclaimed_and_a_live_one_is_not()
    {
        var expired = Key();
        var live = Key();
        await _db.NewClaims().TryClaim(expired, TimeSpan.FromSeconds(-10));
        await _db.NewClaims().TryClaim(live, TimeSpan.FromMinutes(5));

        (await _db.NewClaims().TryClaim(expired, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>();
        (await _db.NewClaims().TryClaim(live, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Lost>();
    }

    [Test]
    public async Task The_sweeper_releases_only_expired_in_flight_claims()
    {
        var stale = Key();
        var live = Key();
        await _db.NewClaims().TryClaim(stale, TimeSpan.FromSeconds(-10));
        await _db.NewClaims().TryClaim(live, TimeSpan.FromMinutes(5));

        (await new EffectClaimSweeper(_db.NewClaims()).Sweep(DateTimeOffset.UtcNow)).Should().Be(1);
    }

    [Test]
    public async Task A_concurrent_create_of_one_draft_is_a_lost_race_not_a_throw()
    {
        var id = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };
        (await _db.NewStore().Upsert("u", id, snapshot)).Should().BeTrue();

        // A writer that read "no row" before the first insert landed inserts the same key.
        (await _db.NewStore().Insert("u", id, snapshot))
            .Should()
            .BeFalse();
    }
}
