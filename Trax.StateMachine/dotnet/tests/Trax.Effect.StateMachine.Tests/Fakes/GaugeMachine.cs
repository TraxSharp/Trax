using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

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
/// A machine whose guards are the numeric comparisons other than <c>GreaterThan</c> and <c>EqualTo</c>: <c>Lower</c>
/// takes a value below 13 (<c>lt</c>), <c>Raise</c> one of at least 2 (<c>gte</c>), and <c>Settle</c> needs a
/// reading of at most 10 (<c>lte</c>). Its samples sit on each side of every boundary, so the differential corpus
/// proves both runtimes compare the same way. Its exported IR is the committed <c>machines/gauge/gauge.ir.json</c>;
/// Trax.Cli's DeclarativeGaugeMachine declares the same machine and regenerates the artifacts.
/// </summary>
public static class GaugeMachine
{
    public sealed record ReadingContext
    {
        public int Reading { get; init; }
    }

    public sealed record ValueInput
    {
        public int Value { get; init; }
    }

    public static readonly BuiltMachine<GaugeState, GaugeTrigger> Built = Build();
    public static SnapshotMachine<GaugeState, GaugeTrigger> Machine => Built.Engine;

    private static BuiltMachine<GaugeState, GaugeTrigger> Build()
    {
        var m = new MachineBuilder<GaugeState, GaugeTrigger>();
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

        // Each comparison sampled below, on and above its boundary. Reading is reached through Lower and Raise,
        // so its readings come from those samples; the probes add the boundary of AtMost(10) itself.
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

        return m.Build();
    }
}
