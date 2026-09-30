using System.Text.Json.Nodes;
using FluentAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Direct coverage of the fluent builder's branches the checkout machine does not exercise: the two
/// Build() validation throws, MigrateFrom, the default effect-key prefix, a transition with no guard or
/// reducer, and a machine with no committed states or effects.
/// </summary>
public class MachineBuilderTests
{
    private enum S
    {
        A,
        B,
    }

    private enum T
    {
        Go,
    }

    private interface IFakeEffect { }

    private static BuiltMachine<S, T> Minimal()
    {
        var b = new MachineBuilder<S, T>();
        b.Id("m").StartsAt(S.A, () => new JsonObject());
        b.In(S.A).On(T.Go).To(S.B);
        return b.Build();
    }

    [Test]
    public void Build_without_an_id_throws_a_helpful_error()
    {
        var b = new MachineBuilder<S, T>();
        b.StartsAt(S.A, () => new JsonObject());

        var act = () => b.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Id(*");
    }

    [Test]
    public void Build_without_a_start_state_throws_a_helpful_error()
    {
        var b = new MachineBuilder<S, T>();
        b.Id("m");

        var act = () => b.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*StartsAt(*");
    }

    [Test]
    public void A_transition_without_a_guard_or_reducer_carries_the_context_forward()
    {
        var built = Minimal();
        var snapshot = built.Engine.Definition.CreateInitialSnapshot() with
        {
            Context = new JsonObject { ["x"] = 1 },
        };

        var result = built.Engine.Advance(snapshot, "Go");

        result.Should().BeOfType<AdvanceResult.Transitioned>().Which.Snapshot.Context["x"]!
            .GetValue<int>()
            .Should()
            .Be(1);
    }

    [Test]
    public void A_machine_with_no_committed_states_or_effects_reports_empty_metadata()
    {
        var built = Minimal();

        built.CommittedStates.Should().BeEmpty();
        built.Effects.Should().BeEmpty();
    }

    [Test]
    public void RunsOnce_without_an_explicit_key_prefix_defaults_to_id_and_trigger()
    {
        var b = new MachineBuilder<S, T>();
        b.Id("m").StartsAt(S.A, () => new JsonObject());
        b.In(S.A).On(T.Go).RunsOnce<IFakeEffect>().To(S.B);

        var built = b.Build();

        built.Effects.Should().ContainSingle();
        built.Effects[0].KeyPrefix.Should().Be("m:Go");
        built.Effects[0].EffectType.Should().Be<IFakeEffect>();
    }

    [Test]
    public void MigrateFrom_upgrades_an_older_snapshot_on_rehydrate()
    {
        var b = new MachineBuilder<S, T>();
        b.Id("m").Version(2).StartsAt(S.A, () => new JsonObject());
        b.In(S.A).On(T.Go).To(S.B);
        b.MigrateFrom(
            1,
            (state, ctx) =>
            {
                var next = (JsonObject)ctx.DeepClone();
                next["migrated"] = true;
                return new MigrationResult(state, next);
            }
        );
        var built = b.Build();

        var result = built.Engine.Rehydrate(
            "{\"machine\":\"m\",\"version\":1,\"state\":\"A\",\"context\":{}}"
        );

        var ok = result.Should().BeOfType<RehydrationResult.Ok>().Which;
        ok.Snapshot.Version.Should().Be(2);
        ok.Snapshot.Context["migrated"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("../x")]
    [TestCase("Checkout")]
    [TestCase("check_out")]
    [TestCase("check--out")]
    [TestCase("-checkout")]
    [TestCase("checkout-")]
    [TestCase("1checkout")]
    public void Id_refuses_an_id_that_is_not_kebab_case(string id)
    {
        var b = new MachineBuilder<S, T>();

        var act = () => b.Id(id);

        act.Should().Throw<ArgumentException>().WithMessage("*kebab-case*");
    }

    [Test]
    public void Id_refuses_a_null_id()
    {
        var b = new MachineBuilder<S, T>();

        var act = () => b.Id(null!);

        act.Should().Throw<ArgumentException>();
    }

    [TestCase("m")]
    [TestCase("checkout")]
    [TestCase("turnstile-two")]
    [TestCase("a1-b2-c3")]
    public void Id_accepts_a_kebab_case_id(string id)
    {
        var b = new MachineBuilder<S, T>();
        b.Id(id).StartsAt(S.A, () => new JsonObject());

        b.Build().Definition.Id.Should().Be(id);
    }

    [Test]
    public void Build_refuses_a_machine_that_binds_more_than_one_effect()
    {
        var b = new MachineBuilder<S, T>();
        b.Id("m").StartsAt(S.A, () => new JsonObject());
        b.In(S.A).On(T.Go).RunsOnce<IFakeEffect>().To(S.B);
        b.In(S.B).On(T.Go).RunsOnce<IFakeEffect>("other").To(S.A);

        var act = () => b.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RunsOnce*one*");
    }
}
