using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

public class MigrationTests
{
    private const string V1Unlocked =
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}";

    [Test]
    public void An_older_snapshot_is_migrated_forward_on_rehydrate()
    {
        var result = TestTurnstileV2.Machine.Rehydrate(V1Unlocked);

        var ok = result.Should().BeOfType<RehydrationResult.Ok>().Which;
        ok.Snapshot.Version.Should().Be(2);
        ok.Snapshot.Context["paidWith"]!.GetValue<string>().Should().Be("quarter");
        ok.Snapshot.Context["migrated"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public void A_gap_in_the_migration_chain_is_a_version_mismatch()
    {
        // V3WithGap can migrate 1 -> 2 but not 2 -> 3, so a v1 snapshot cannot reach v3.
        TestTurnstileV2
            .V3WithGap.Rehydrate(V1Unlocked)
            .Should()
            .BeOfType<RehydrationResult.Error>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.VersionMismatch);
    }

    [Test]
    public void A_snapshot_newer_than_the_definition_is_a_version_mismatch()
    {
        var v2 = "{\"machine\":\"turnstile\",\"version\":2,\"state\":\"Locked\",\"context\":{}}";

        TestTurnstile
            .Machine.Rehydrate(v2)
            .Should()
            .BeOfType<RehydrationResult.Error>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.VersionMismatch);
    }
}
