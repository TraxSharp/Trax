using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum RangeState
{
    Open,
    Ranged,
}

public enum RangeTrigger
{
    Build,
    Poke,
    Back,
}

/// <summary>
/// A declarative machine whose <c>Build</c> takes a <see cref="RangeInput"/>: two integers, a nullable note and a
/// non-empty list of tags. <c>Poke</c> declares no input. The context of Open holds a non-empty <c>tags</c> list
/// too, so a constraint read from the context instead of the input would pass.
/// </summary>
public static class RangeMachine
{
    public sealed record RangeInput
    {
        public int From { get; init; }
        public int To { get; init; }
        public string? Note { get; init; }

        [MinLength(1)]
        public List<string> Tags { get; init; } = [];
    }

    public sealed record TagsContext
    {
        [MinLength(1)]
        public List<string> Tags { get; init; } = [];
    }

    public static readonly BuiltMachine<RangeState, RangeTrigger> Built = Build();
    public static SnapshotMachine<RangeState, RangeTrigger> Machine => Built.Engine;

    private static JsonObject Seed() => new() { ["tags"] = new JsonArray("seed") };

    private static BuiltMachine<RangeState, RangeTrigger> Build()
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("range").Version(1).StartsAt(RangeState.Open, Seed);

        m.In(RangeState.Open)
            .Context<TagsContext>()
            .On(RangeTrigger.Build)
            .WithInput<RangeInput>()
            .When(Input((RangeInput i) => i.From).GreaterThan(-1))
            .Because("From must not be negative.")
            .To(RangeState.Ranged)
            .On(RangeTrigger.Poke)
            .To(RangeState.Open);

        m.In(RangeState.Ranged).Context<TagsContext>().On(RangeTrigger.Back).To(RangeState.Open);

        return m.Build();
    }

    public static Snapshot Open() =>
        new()
        {
            Machine = "range",
            Version = 1,
            State = nameof(RangeState.Open),
            Context = Seed(),
        };

    public static Snapshot Ranged() => Open() with { State = nameof(RangeState.Ranged) };

    /// <summary>An input every field of which is valid; tests change one thing about it.</summary>
    public static JsonObject ValidInput() =>
        new()
        {
            ["from"] = 1,
            ["to"] = 2,
            ["note"] = null,
            ["tags"] = new JsonArray("a"),
        };
}
