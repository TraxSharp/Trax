using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>
/// A machine authored only to exercise the IR exporter across every rule kind, reducer kind, comparison
/// operator, an effect binding, and a committed state. It is never run (some rules/reducers are custom with
/// no handler), only exported, so the exporter's full surface is covered.
/// </summary>
public static class RichExportMachine
{
    public enum RState
    {
        A,
        B,
    }

    public enum RTrigger
    {
        Set1,
        Set2,
        Clr,
        Rst,
        Kp,
        Cst,
    }

    public sealed record Ctx
    {
        [MinLength(1)]
        public string Name { get; init; } = "";
        public int Total { get; init; }
        public string[] Items { get; init; } = [];
        public string? Note { get; init; }
    }

    private sealed class ThingEffect { }

    public static readonly BuiltMachine<RState, RTrigger> Built = Build();

    private static BuiltMachine<RState, RTrigger> Build()
    {
        var m = new MachineBuilder<RState, RTrigger>();
        m.Id("rich")
            .StartsAt(
                RState.A,
                () =>
                    new JsonObject
                    {
                        ["name"] = "x",
                        ["total"] = 0,
                        ["items"] = new JsonArray(),
                        ["note"] = null,
                    }
            );

        m.In(RState.A)
            .Context<Ctx>()
            .On(RTrigger.Set1)
            .When(
                All(
                    new Rule.Present(RuleSource.Context, "total"),
                    new Rule.Absent(RuleSource.Context, "note"),
                    new Rule.OfType(RuleSource.Context, "total", JsonFieldType.Number),
                    new Rule.NonEmpty(RuleSource.Context, "name"),
                    new Rule.OneOf(RuleSource.Input, "kind", ["x"]),
                    new Rule.Compare(RuleSource.Context, "total", CompareOp.GreaterThan, 0),
                    new Rule.Compare(RuleSource.Context, "total", CompareOp.GreaterOrEqual, 0),
                    new Rule.Compare(RuleSource.Context, "total", CompareOp.LessThan, 9),
                    new Rule.Compare(RuleSource.Context, "total", CompareOp.LessOrEqual, 9),
                    new Rule.Compare(RuleSource.Context, "total", CompareOp.EqualTo, 0),
                    new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterThan, -1),
                    Any(new Rule.Custom("c"))
                )
            )
            .Reduce(Set((Ctx c) => c.Total).ToValue(1))
            .To(RState.B)
            .On(RTrigger.Set2)
            .Reduce(new Reduction.Set([new SetStep("name", new ValueSource.FromInput("kind"))]))
            .To(RState.B)
            .On(RTrigger.Clr)
            .Reduce(new Reduction.Clear())
            .To(RState.B)
            .On(RTrigger.Rst)
            .Reduce(new Reduction.Reset())
            .To(RState.B)
            .On(RTrigger.Kp)
            .Reduce(new Reduction.Keep())
            .To(RState.B)
            .On(RTrigger.Cst)
            .When(new Rule.Custom("always"))
            .RunsOnce<ThingEffect>()
            .Reduce(new Reduction.Custom("fresh"))
            .To(RState.B);

        m.In(RState.B).Committed().Context<Ctx>();

        return m.Build();
    }
}
