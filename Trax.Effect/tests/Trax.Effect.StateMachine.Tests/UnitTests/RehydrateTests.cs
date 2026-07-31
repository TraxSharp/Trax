using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

public class RehydrateTests
{
    private static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Machine =
        TestTurnstile.Machine;

    private static string? Ok(string json) =>
        Machine.Rehydrate(json) is RehydrationResult.Ok ok ? ok.Snapshot.State : null;

    private static string? ErrorCode(string json) =>
        Machine.Rehydrate(json) is RehydrationResult.Error err ? err.Code : null;

    [Test]
    public void A_well_formed_Locked_snapshot_rehydrates()
    {
        Ok("{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}")
            .Should()
            .Be("Locked");
    }

    [Test]
    public void A_well_formed_Unlocked_snapshot_rehydrates()
    {
        Ok(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"
            )
            .Should()
            .Be("Unlocked");
    }

    [TestCase("not json at all", RehydrationErrorCodes.Malformed)]
    [TestCase("[1,2,3]", RehydrationErrorCodes.Malformed)]
    [TestCase("42", RehydrationErrorCodes.Malformed)]
    [TestCase(
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\"}",
        RehydrationErrorCodes.Malformed
    )]
    [TestCase(
        "{\"machine\":\"other\",\"version\":1,\"state\":\"Locked\",\"context\":{}}",
        RehydrationErrorCodes.UnknownMachine
    )]
    [TestCase(
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Nowhere\",\"context\":{}}",
        RehydrationErrorCodes.UnknownState
    )]
    [TestCase(
        "{\"machine\":\"turnstile\",\"version\":2,\"state\":\"Locked\",\"context\":{}}",
        RehydrationErrorCodes.VersionMismatch
    )]
    [TestCase(
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{\"paidWith\":\"quarter\"}}",
        RehydrationErrorCodes.InvalidContext
    )]
    [TestCase(
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{}}",
        RehydrationErrorCodes.InvalidContext
    )]
    public void Rehydrate_maps_bad_input_to_the_right_error_code(string json, string expected)
    {
        ErrorCode(json).Should().Be(expected);
    }

    // JSON has one number type; TypeScript's `Number.isInteger` cannot tell 1 from 1.0 from 1e0, so the
    // C# reader must accept all three and normalize to 1. A non-integral version is "missing/invalid".
    [TestCase("1")]
    [TestCase("1.0")]
    [TestCase("1e0")]
    public void Rehydrate_accepts_any_integral_version_and_normalizes_it(string version)
    {
        var json =
            $"{{\"machine\":\"turnstile\",\"version\":{version},\"state\":\"Locked\",\"context\":{{}}}}";

        var result = Machine.Rehydrate(json);

        var ok = result.Should().BeOfType<RehydrationResult.Ok>().Which;
        ok.Snapshot.Version.Should().Be(1);
    }

    [TestCase("1.5")]
    [TestCase("1e400")]
    public void Rehydrate_treats_a_non_integral_or_out_of_range_version_as_malformed(string version)
    {
        var json =
            $"{{\"machine\":\"turnstile\",\"version\":{version},\"state\":\"Locked\",\"context\":{{}}}}";

        ErrorCode(json).Should().Be(RehydrationErrorCodes.Malformed);
    }
}
