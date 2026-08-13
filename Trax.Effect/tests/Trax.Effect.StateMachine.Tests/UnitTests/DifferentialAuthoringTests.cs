using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The <c>.Differential(...)</c> authoring surface and its IR export: every input kind (typed and raw
/// samples, seeds, probe contexts), the serialization guard, the declarative-only guard, and the
/// <see cref="DifferentialModel{TState,TTrigger}"/> emptiness check.
/// </summary>
public class DifferentialAuthoringTests
{
    // A declarative turnstile that authors every differential kind: a raw sample, a typed and a raw seed, and
    // a typed and a raw probe context. Reuses DeclarativeTurnstile's context/input records.
    private static BuiltMachine<TurnstileState, TurnstileTrigger> BuildRich()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("rich-diff").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<DeclarativeTurnstile.CoinInput>()
            .When(Input((DeclarativeTurnstile.CoinInput i) => i.Coin).IsOneOf("quarter"))
            .Reduce(
                Set((DeclarativeTurnstile.UnlockedContext u) => u.PaidWith)
                    .FromInput((DeclarativeTurnstile.CoinInput i) => i.Coin)
            )
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<DeclarativeTurnstile.UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);

        m.Differential(d =>
            d.Sample(TurnstileTrigger.Coin, new JsonObject { ["coin"] = "raw" })
                .Seed(
                    TurnstileState.Unlocked,
                    new DeclarativeTurnstile.UnlockedContext { PaidWith = "seed" }
                )
                .Seed(TurnstileState.Locked, new JsonObject())
                .Probe(new DeclarativeTurnstile.UnlockedContext { PaidWith = "probe" })
                .Probe(new JsonObject { ["paidWith"] = "raw-probe" })
        );

        return m.Build();
    }

    [Test]
    public void Export_carries_samples_seeds_and_contexts()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(BuildRich()))!;
        var diff = (JsonObject)ir["differential"]!;

        // Raw sample under its trigger.
        var coin = ((JsonObject)diff["samples"]!)["Coin"]!.AsArray();
        coin.Should().HaveCount(1);
        ((JsonObject)coin[0]!)["coin"]!.GetValue<string>().Should().Be("raw");

        // Typed seed serializes camelCase; raw empty seed stays {}.
        var seeds = (JsonObject)diff["seeds"]!;
        seeds["Unlocked"]!["paidWith"]!.GetValue<string>().Should().Be("seed");
        seeds["Locked"]!.AsObject().Count.Should().Be(0);

        // Both probe contexts land in `contexts`, typed then raw.
        var contexts = diff["contexts"]!.AsArray();
        contexts.Should().HaveCount(2);
        ((JsonObject)contexts[0]!)["paidWith"]!.GetValue<string>().Should().Be("probe");
        ((JsonObject)contexts[1]!)["paidWith"]!.GetValue<string>().Should().Be("raw-probe");
    }

    [Test]
    public void Sample_of_a_value_that_is_not_a_json_object_throws()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("x").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        // An int serializes to a JSON value, not an object — the typed overload rejects it.
        var act = () => m.Differential(d => d.Sample(TurnstileTrigger.Coin, 5));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*must serialize to a JSON object*");
    }

    [Test]
    public void Differential_on_a_raw_delegate_machine_throws_on_build()
    {
        // A machine authored with raw delegates (Holds / When(delegate)) is not declarative, so it can't
        // export an IR — and .Differential rides the IR. Build must reject the combination.
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("x").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Holds(_ => null)
            .On(TurnstileTrigger.Coin)
            .When((_, _) => true)
            .To(TurnstileState.Unlocked);
        m.Differential(d => d.Sample(TurnstileTrigger.Coin, new JsonObject()));

        var act = () => m.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*only valid on a declaratively-authored machine*");
    }

    [Test]
    public void DifferentialModel_IsEmpty_is_true_only_when_every_collection_is_empty()
    {
        var empty = new DifferentialModel<TurnstileState, TurnstileTrigger>(
            new Dictionary<TurnstileTrigger, IReadOnlyList<JsonNode>>(),
            new Dictionary<TurnstileState, JsonNode>(),
            []
        );
        empty.IsEmpty.Should().BeTrue();

        var nonEmpty = empty with { Contexts = [new JsonObject()] };
        nonEmpty.IsEmpty.Should().BeFalse();
    }
}
