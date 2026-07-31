using FluentAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Stress.Fixtures;

namespace Trax.Effect.StateMachine.Tests.Stress.IntegrationTests;

/// <summary>
/// The claim ledger under heavy contention: many keys each stormed by a crowd of claimants, and expired
/// leases reclaimed under a storm. The unique key is the lock; the fence token guards completion. At scale,
/// exactly one claimant wins each key.
/// </summary>
[TestFixture]
public class ClaimContentionStressTests : StressFixture
{
    private static async Task<ClaimResult> Claim(string key, TimeSpan lease)
    {
        await using var ctx = StressDb.NewContext();
        return await new EfEffectClaimStore(ctx).TryClaim(key, lease);
    }

    [Test]
    public async Task Concurrent_claims_across_many_keys_yield_exactly_one_winner_each()
    {
        var keys = StressProfile.ClaimKeys;
        var claimants = StressProfile.ClaimantsPerKey;
        var run = Guid.NewGuid().ToString("N");

        var results = await Fan(
            keys * claimants,
            StressProfile.MaxConcurrency,
            k => Claim($"claim:{run}:{k / claimants}", TimeSpan.FromMinutes(5))
        );

        var wins = results.Count(r => r is ClaimResult.Won);
        var losses = results.Count(r => r is ClaimResult.Lost);

        wins.Should().Be(keys, "exactly one claimant wins each of {0} keys", keys);
        losses.Should().Be(keys * (claimants - 1), "everyone else loses");

        TestContext.Progress.WriteLine(
            $"[claims] {keys} keys x {claimants} claimants = {keys * claimants} claims, winners={wins}, losers={losses}"
        );
    }

    [Test]
    public async Task An_expired_lease_under_a_reclaim_storm_is_reclaimed_by_exactly_one()
    {
        var keys = StressProfile.ClaimKeys;
        var claimants = StressProfile.ClaimantsPerKey;
        var run = Guid.NewGuid().ToString("N");

        // Seed each key with an in-flight claim whose lease has already expired (a claimant that won and
        // then vanished without completing).
        await Fan(
            keys,
            StressProfile.MaxConcurrency,
            async i =>
            {
                (await Claim($"reclaim:{run}:{i}", TimeSpan.FromSeconds(-10)))
                    .Should()
                    .BeOfType<ClaimResult.Won>();
                return 0;
            }
        );

        // Storm every expired key. Exactly one claimant reclaims each; the rest lose.
        var results = await Fan(
            keys * claimants,
            StressProfile.MaxConcurrency,
            k => Claim($"reclaim:{run}:{k / claimants}", TimeSpan.FromMinutes(5))
        );

        results
            .Count(r => r is ClaimResult.Won)
            .Should()
            .Be(keys, "exactly one claimant reclaims each expired key");
    }
}
