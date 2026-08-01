using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The machine-agnostic handle a host discovers and the registry keys on.</summary>
public interface IMachine
{
    /// <summary>The machine's stable id (from the fluent <c>Id(...)</c>).</summary>
    string Name { get; }

    /// <summary>Whether the machine binds an irreversible exactly-once effect.</summary>
    bool HasEffect { get; }

    /// <summary>Build the draft service for a request's store (threading committed states, the effect-claim reset, and the optional draft TTL).</summary>
    ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl = null
    );

    /// <summary>Build the exactly-once effect runner (resolving the effect from the container), or null if none.</summary>
    ISnapshotEffectRunner? CreateEffectRunner(
        ISnapshotDraftService service,
        IdempotentEffect idempotent,
        IServiceProvider services
    );
}

/// <summary>
/// The base class a machine subclasses. Override <see cref="Configure"/> to declare the machine fluently
/// (states, transitions, guards, reducers, committed states, and the one irreversible effect), and a host
/// discovers it and wires everything, no per-machine registration, no effect wiring in the composition root.
///
/// <code>
/// public sealed class Checkout : Machine&lt;CheckoutState, CheckoutTrigger&gt;
/// {
///     protected override void Configure(IMachineBuilder&lt;CheckoutState, CheckoutTrigger&gt; m)
///     {
///         m.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, FreshCart);
///         m.In(CheckoutState.Review).On(CheckoutTrigger.Pay).When(Payable).RunsOnce&lt;ICharge&gt;().Reduce(ApplyReceipt).To(CheckoutState.Paid);
///         m.In(CheckoutState.Paid).Committed();
///     }
/// }
/// </code>
/// </summary>
public abstract class Machine<TState, TTrigger> : IMachine
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private BuiltMachine<TState, TTrigger>? _built;
    private BuiltMachine<TState, TTrigger> Built => _built ??= BuildOnce();

    private BuiltMachine<TState, TTrigger> BuildOnce()
    {
        var builder = new MachineBuilder<TState, TTrigger>();
        Configure(builder);
        return builder.Build();
    }

    /// <summary>Declare the machine. Everything about it lives here, on the transitions it belongs to.</summary>
    protected abstract void Configure(IMachineBuilder<TState, TTrigger> machine);

    public string Name => Built.Definition.Id;

    public bool HasEffect => Built.Effects.Count > 0;

    public ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl = null
    ) =>
        new SnapshotDraftService<TState, TTrigger>(
            Built.Engine,
            store,
            Built.CommittedStates,
            claims,
            EffectKeysOnReset,
            draftTtl
        );

    private IEnumerable<string> EffectKeysOnReset(string userKey, Guid id) =>
        Built.Effects.Select(e => $"{e.KeyPrefix}:{userKey}:{id}");

    public ISnapshotEffectRunner? CreateEffectRunner(
        ISnapshotDraftService service,
        IdempotentEffect idempotent,
        IServiceProvider services
    )
    {
        if (Built.Effects.Count == 0)
            return null;

        var binding = Built.Effects[0];
        var effect = (IEffect)services.GetRequiredService(binding.EffectType);
        return new SnapshotEffectRunner<TState, TTrigger>(
            (SnapshotDraftService<TState, TTrigger>)service,
            effect,
            idempotent,
            binding.From,
            binding.Trigger,
            binding.To,
            (userKey, id) => $"{binding.KeyPrefix}:{userKey}:{id}",
            receiptKey: "receipt"
        );
    }
}
