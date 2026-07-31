using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public class EffectClaimStoreTests
{
    private static string Key() => $"claim:{Guid.NewGuid()}";

    private static Guid Won(ClaimResult result)
    {
        result.Should().BeOfType<ClaimResult.Won>();
        return ((ClaimResult.Won)result).OwnerToken;
    }

    [Test]
    public async Task Claiming_once_wins_and_a_second_active_claim_loses()
    {
        var key = Key();
        var first = await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5));
        var second = await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5));

        first.Should().BeOfType<ClaimResult.Won>();
        second.Should().BeOfType<ClaimResult.Lost>();
    }

    [Test]
    public async Task Complete_records_the_receipt_only_under_the_owning_fence_token()
    {
        var key = Key();
        var owner = Won(await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)));

        (await TestDb.NewClaims().Complete(key, Guid.NewGuid(), "wrong")).Should().BeFalse();
        (await TestDb.NewClaims().GetReceipt(key)).Should().BeNull();

        (await TestDb.NewClaims().Complete(key, owner, "receipt-1")).Should().BeTrue();
        (await TestDb.NewClaims().GetReceipt(key)).Should().Be("receipt-1");
    }

    [Test]
    public async Task Release_frees_the_key_and_is_a_no_op_on_a_missing_key()
    {
        var key = Key();
        await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5));

        await TestDb.NewClaims().Release(key);
        (await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>();

        // Releasing an absent key is harmless.
        var act = async () => await TestDb.NewClaims().Release($"never:{Guid.NewGuid()}");
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task An_in_flight_claim_whose_lease_expired_is_reclaimed_with_a_new_fence_token()
    {
        var key = Key();
        // Claim with an already-expired lease (zero) so it is immediately reclaimable.
        var first = Won(await TestDb.NewClaims().TryClaim(key, TimeSpan.FromSeconds(-10)));

        var second = await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5));
        second.Should().BeOfType<ClaimResult.Won>();
        ((ClaimResult.Won)second).OwnerToken.Should().NotBe(first);
    }

    [Test]
    public async Task A_stuck_runner_that_revives_after_reclaim_is_fenced_out_of_completing()
    {
        var key = Key();
        var stuck = Won(await TestDb.NewClaims().TryClaim(key, TimeSpan.FromSeconds(-10))); // expired immediately
        var reclaimer = Won(await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)));

        // The revived stuck runner cannot complete or release the reclaimer's claim.
        (await TestDb.NewClaims().Complete(key, stuck, "stale"))
            .Should()
            .BeFalse();
        (await TestDb.NewClaims().ReleaseOwned(key, stuck)).Should().BeFalse();

        // The reclaimer completes cleanly.
        (await TestDb.NewClaims().Complete(key, reclaimer, "fresh"))
            .Should()
            .BeTrue();
        (await TestDb.NewClaims().GetReceipt(key)).Should().Be("fresh");
    }

    [Test]
    public async Task Sixteen_concurrent_claims_yield_exactly_one_winner()
    {
        var key = Key();
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(_ =>
                    Task.Run(() => TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)))
                )
        );

        results.Count(r => r is ClaimResult.Won).Should().Be(1);
        results.Count(r => r is ClaimResult.Lost).Should().Be(15);
    }

    [Test]
    public async Task The_sweeper_releases_stale_in_flight_claims_but_leaves_completed_ones()
    {
        var stale = Key();
        var done = Key();
        await TestDb.NewClaims().TryClaim(stale, TimeSpan.FromSeconds(-10)); // in-flight, expired
        var owner = Won(await TestDb.NewClaims().TryClaim(done, TimeSpan.FromMinutes(5)));
        await TestDb.NewClaims().Complete(done, owner, "kept");

        var swept = await new EffectClaimSweeper(TestDb.NewClaims()).Sweep(DateTimeOffset.UtcNow);

        swept.Should().BeGreaterThanOrEqualTo(1);
        (await TestDb.NewClaims().GetReceipt(done)).Should().Be("kept"); // completed claim survives
        (await TestDb.NewClaims().TryClaim(stale, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>(); // freed
    }
}
