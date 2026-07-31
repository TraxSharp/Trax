using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Covers the engine's reachable-but-easily-missed defensive paths: the rehydrate totality backstop
/// (an exception escaping the core still degrades to a typed error) and <c>CanFire</c> over unknown
/// state/trigger tokens.
/// </summary>
public class EngineDefensiveTests
{
    private static Snapshot Locked() =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };

    [Test]
    public void CanFire_is_false_for_an_unknown_state_token()
    {
        TestTurnstile
            .Machine.CanFire(Locked() with { State = "Broken" }, "Coin")
            .Should()
            .BeFalse();
    }

    [Test]
    public void CanFire_is_false_for_an_unknown_trigger_token()
    {
        TestTurnstile.Machine.CanFire(Locked(), "Zap").Should().BeFalse();
    }

    [Test]
    public void Equal_snapshots_hash_equally()
    {
        var a = Locked();
        var b = Locked();

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        new HashSet<Snapshot> { a, b }
            .Should()
            .HaveCount(1);
    }

    [Test]
    public void Rehydrate_backstops_an_exception_thrown_from_a_migration_as_malformed()
    {
        // A migration that throws makes RehydrateCore throw; the outer totality backstop must catch it and
        // return a typed error rather than letting it escape.
        var builder = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        builder.Id("turnstile").Version(2).StartsAt(TurnstileState.Locked, () => new JsonObject());
        builder.In(TurnstileState.Locked).On(TurnstileTrigger.Coin).To(TurnstileState.Unlocked);
        builder.MigrateFrom(1, (_, _) => throw new InvalidOperationException("boom"));
        var engine = builder.Build().Engine;

        RehydrationResult result = null!;
        var act = () =>
            result = engine.Rehydrate(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}"
            );

        act.Should().NotThrow();
        result
            .Should()
            .BeOfType<RehydrationResult.Error>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.Malformed);
    }
}
