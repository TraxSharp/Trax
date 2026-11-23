using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// A guard: its rule, and the words a Given or a Then says it holds or fails in. A guard whose rule reads only the
/// trigger's input has no such words (<see cref="Holds"/> and <see cref="Fails"/> may be null): a When shows it.
/// </summary>
internal sealed record GlossaryGuard(string Name, string? Holds, string? Fails, JsonObject Rule)
{
    /// <summary>Whether the guard reads nothing but the trigger's input, so only a When's value decides it.</summary>
    internal bool OnInput => ReadsOnlyInput(IrMachine.ReadRule(Rule));

    internal static bool ReadsOnlyInput(StateMachine.Rule rule) =>
        rule switch
        {
            StateMachine.Rule.All all => all.Rules.Count > 0 && all.Rules.All(ReadsOnlyInput),
            StateMachine.Rule.Any any => any.Rules.Count > 0 && any.Rules.All(ReadsOnlyInput),
            StateMachine.Rule.Custom => false,
            StateMachine.Rule.Present r => r.Source == RuleSource.Input,
            StateMachine.Rule.Absent r => r.Source == RuleSource.Input,
            StateMachine.Rule.OfType r => r.Source == RuleSource.Input,
            StateMachine.Rule.NonEmpty r => r.Source == RuleSource.Input,
            StateMachine.Rule.OneOf r => r.Source == RuleSource.Input,
            StateMachine.Rule.Compare r => r.Source == RuleSource.Input,
            StateMachine.Rule.Count r => r.Source == RuleSource.Input,
            StateMachine.Rule.Length r => r.Source == RuleSource.Input,
            StateMachine.Rule.BoolEquals r => r.Source == RuleSource.Input,
            StateMachine.Rule.ArrayOf r => r.Source == RuleSource.Input,
            _ => false,
        };
}
