using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

public enum GaugeState
{
    Idle,
    Reading,
}

public enum GaugeTrigger
{
    Lower,
    Raise,
    Settle,
}

/// <summary>
/// A machine whose guards are <c>LessThan</c>, <c>AtLeast</c> and <c>AtMost</c> (<c>lt</c>, <c>gte</c>,
/// <c>lte</c>), sampled on each side of every bound, so the committed differential corpus holds both runtimes to
/// the same comparisons. Declared as the Trax.Effect tests' GaugeMachine is, whose exported IR is the committed
/// gauge.ir.json; regenerate the artifacts from this one with <c>trax machine generate</c>.
/// </summary>
public sealed class DeclarativeGaugeMachine : Machine<GaugeState, GaugeTrigger>
{
    public sealed record ReadingContext
    {
        public int Reading { get; init; }
    }

    public sealed record ValueInput
    {
        public int Value { get; init; }
    }

    protected override void Configure(IMachineBuilder<GaugeState, GaugeTrigger> m)
    {
        m.Id("gauge").Version(1).StartsAt(GaugeState.Idle, () => new JsonObject());

        m.In(GaugeState.Idle)
            .Context()
            .On(GaugeTrigger.Lower)
            .WithInput<ValueInput>()
            .When(Input((ValueInput i) => i.Value).LessThan(13))
            .Because("A lowered reading is below 13.")
            .Reduce(Set((ReadingContext c) => c.Reading).FromInput((ValueInput i) => i.Value))
            .To(GaugeState.Reading)
            .On(GaugeTrigger.Raise)
            .WithInput<ValueInput>()
            .When(Input((ValueInput i) => i.Value).AtLeast(2))
            .Because("A raised reading is at least 2.")
            .Reduce(Set((ReadingContext c) => c.Reading).FromInput((ValueInput i) => i.Value))
            .To(GaugeState.Reading);

        m.In(GaugeState.Reading)
            .Context<ReadingContext>()
            .On(GaugeTrigger.Settle)
            .When(Field((ReadingContext c) => c.Reading).AtMost(10))
            .Because("Only a reading of at most 10 settles.")
            .Reduce(Clear())
            .To(GaugeState.Idle);

        m.Differential(d =>
            d.Sample(GaugeTrigger.Lower, new ValueInput { Value = 12 })
                .Sample(GaugeTrigger.Lower, new ValueInput { Value = 13 })
                .Sample(GaugeTrigger.Lower, new ValueInput { Value = 14 })
                .Sample(GaugeTrigger.Raise, new ValueInput { Value = 1 })
                .Sample(GaugeTrigger.Raise, new ValueInput { Value = 2 })
                .Sample(GaugeTrigger.Raise, new ValueInput { Value = 3 })
                .Probe(new ReadingContext { Reading = 10 })
                .Probe(new ReadingContext { Reading = 11 })
        );
    }
}
