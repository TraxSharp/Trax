using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The turnstile authored as a discoverable <see cref="Machine{TState,TTrigger}"/> using the DECLARATIVE,
/// string-free surface (context records + member-expression guards + declarative reducers). Unlike
/// <see cref="TurnstileMachine"/> (raw delegates, which cannot export an IR), this one carries the
/// declarative data the exporter needs, so <see cref="IMachine.ExportIr"/> works. The fluent body is shared
/// with the test via <see cref="ConfigureTurnstile"/> so the base-class export can be byte-compared against a
/// direct <c>IrExporter.Export</c> of the identically-configured machine, no golden duplicated.
/// </summary>
public sealed class DeclarativeTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public sealed record UnlockedContext
    {
        [MinLength(1)]
        public string PaidWith { get; init; } = "";
    }

    public sealed record CoinInput
    {
        public string Coin { get; init; } = "";
    }

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        ConfigureTurnstile(m);

    /// <summary>The declarative definition, shared so a test can build the same machine standalone.</summary>
    internal static void ConfigureTurnstile(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        ConfigureTurnstile(m, "declarative-turnstile");

    /// <summary>The same declarative turnstile under a caller-chosen id, so a test can register several without id clashes.</summary>
    internal static void ConfigureTurnstile(
        IMachineBuilder<TurnstileState, TurnstileTrigger> m,
        string id
    )
    {
        // A distinct id from the raw-delegate TurnstileMachine in this assembly, so both coexist under the
        // AddStateMachines registry scan (which keys machines by id).
        m.Id(id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}
