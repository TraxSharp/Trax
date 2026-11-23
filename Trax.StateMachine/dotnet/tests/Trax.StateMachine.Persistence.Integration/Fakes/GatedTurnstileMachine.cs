using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A turnstile whose <c>Coin</c> requires the <c>Editor</c> role, for the refusal a client's rule produces.
/// </summary>
public sealed class GatedTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "gated-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .Requires("Editor")
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked).Context().On(TurnstileTrigger.Push).To(TurnstileState.Locked);
    }

    /// <summary>A snapshot of this machine in <paramref name="state"/>, as the client sends it.</summary>
    public static string Json(string state) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
