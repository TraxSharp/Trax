using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The machine-agnostic handle a host discovers and the registry keys on.</summary>
public interface IMachine
{
    /// <summary>The machine's stable id (from the fluent <c>Id(...)</c>).</summary>
    string Name { get; }

    /// <summary>Whether the machine binds an irreversible exactly-once effect.</summary>
    bool HasEffect { get; }

    /// <summary>
    /// Export this machine's neutral IR (the formalized machine.json) as canonical single-line JSON: the
    /// single-source artifact every frontend generator consumes. Requires a declaratively-authored machine
    /// (<c>.Context</c>/<c>.When</c>/<c>.Reduce</c>); a raw-delegate machine throws, because its guards and
    /// reducers are opaque closures with no exportable data. This is the in-process entry point the
    /// <c>trax machine</c> CLI calls.
    /// </summary>
    string ExportIr();

    /// <summary>
    /// A stable content hash (lowercase hex SHA-256) of this machine's exported IR (<see cref="ExportIr"/>) —
    /// the cross-language identity of its behavioural contract. The generated frontend twin embeds the same
    /// hash, computed from the same committed IR, so a client and the server can detect at runtime that they
    /// are running different machine definitions (version skew) and refuse to silently disagree on a transition.
    /// </summary>
    string? SchemaHash { get; }

    /// <summary>
    /// The machine's committed differential corpus (the TypeScript oracle's golden JSON), or null if it ships
    /// none. A host replays it at startup via <see cref="SelfCheck"/> to prove the running C# engine still
    /// reproduces the frontend twin's behaviour. A machine that carries a corpus loads it (e.g. from an
    /// embedded resource).
    /// </summary>
    string? Corpus { get; }

    /// <summary>
    /// Replay this machine's <see cref="Corpus"/> through its own engine and return one human-readable diff per
    /// case it fails to reproduce — empty means exact agreement (and empty when the machine ships no corpus).
    /// This is the same proof the differential test runs, callable at startup as a self-check.
    /// </summary>
    IReadOnlyList<string> SelfCheck();

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

    public string ExportIr() => IrExporter.Export(Built);

    private string? _schemaHash;
    private bool _schemaHashComputed;

    public string? SchemaHash
    {
        get
        {
            if (_schemaHashComputed)
                return _schemaHash;
            _schemaHashComputed = true;
            try
            {
                _schemaHash = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(ExportIr())
                    )
                );
            }
            catch (InvalidOperationException)
            {
                // A raw-delegate machine has no exportable IR (its guards/reducers are opaque closures), so it
                // has no frontend twin, no schema hash, and no handshake. Null, not throw: the guard treats it
                // as "no check" for a client that somehow sends a hash for it.
                _schemaHash = null;
            }
            return _schemaHash;
        }
    }

    /// <summary>Override to ship a committed differential corpus (e.g. an embedded resource); null = none.</summary>
    public virtual string? Corpus => null;

    public IReadOnlyList<string> SelfCheck() =>
        Corpus is { } corpus ? CorpusReplay.Replay(Built.Engine, corpus) : Array.Empty<string>();

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
        var effect = (ISnapshotEffect)services.GetRequiredService(binding.EffectType);
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
