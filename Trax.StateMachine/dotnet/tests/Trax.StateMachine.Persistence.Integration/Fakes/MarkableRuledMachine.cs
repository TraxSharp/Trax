using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// Declares every rule, and is left open so a test can mark a subclass <c>[TraxMachine]</c> in an assembly it builds:
/// the attribute on a type in this assembly would serve it in every host that scans it.
/// </summary>
public class MarkableRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated
        );
}
