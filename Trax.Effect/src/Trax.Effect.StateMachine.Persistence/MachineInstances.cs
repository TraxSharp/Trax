using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Creates the instances of a machine that the system owns rather than a user: one per partition of a source, say,
/// each driven by the trains its states invoke. They are created only from code (a train, or startup); no GraphQL
/// operation creates, reads or advances one, and no user's request reaches one, even under the same id.
/// </summary>
public interface IMachineInstances
{
    /// <summary>
    /// Creates the system-owned instance of <typeparamref name="TMachine"/> for <paramref name="key"/> in the
    /// machine's initial state with <paramref name="context"/>, or returns the one that already exists. The id is
    /// derived from the machine's name and the key (<see cref="MachineInstanceId.For"/>), so calling this twice, or
    /// from two hosts at once, finds one instance: the second call creates nothing and returns the first's.
    /// </summary>
    /// <typeparam name="TMachine">A machine registered through <c>AddStateMachines</c>.</typeparam>
    /// <param name="key">What the instance is for: one or more parts, such as a source and a partition.</param>
    /// <param name="context">
    /// The initial context, validated against the initial state like any snapshot. Null uses the context the
    /// machine declares with <c>StartsAt</c>. Ignored when the instance already exists.
    /// </param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>The instance: its id, its machine, the state it is in now, and whether this call created it.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMachine"/> is not registered, does not declare <c>SystemOwned()</c>, or its initial state
    /// invokes a train whose run could not be queued.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="context"/> is not a valid context for the initial state, or makes the snapshot larger than
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/>.
    /// </exception>
    Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine;

    /// <summary>
    /// Fires <paramref name="trigger"/> on the system-owned instance of <typeparamref name="TMachine"/> for
    /// <paramref name="key"/>, as the system: the code path through which a system instance moves other than by its
    /// trains' outcomes, such as a <c>Retry</c> out of a failure state. It is checked exactly as a user's advance is:
    /// the transition's guard and the target state's context rule, the 64 KiB cap, and the reserved triggers, so an
    /// invoked train's outcome trigger is refused as <c>outcome-bound</c> and an effect-bound one as
    /// <c>effect-bound</c>. Entering or leaving an invoking state queues or cancels the run in the same transaction,
    /// as an advance does; the run is authorized in the trusted scope, as every run of a system-owned machine is.
    /// </summary>
    /// <remarks>
    /// No GraphQL operation calls this; a host that lets a person retry a system instance writes its own operation
    /// over it, under its own authorization.
    /// </remarks>
    /// <typeparam name="TMachine">A system-owned machine registered through <c>AddStateMachines</c>.</typeparam>
    /// <param name="key">The instance's key, as it was started with.</param>
    /// <param name="trigger">The trigger to fire, by name.</param>
    /// <param name="input">The trigger's input, when it takes one.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>
    /// <see cref="AdvanceOutcome.Advanced"/> with the new snapshot; <see cref="AdvanceOutcome.NotFound"/> when no
    /// instance exists for the key; <see cref="AdvanceOutcome.Rejected"/> with the engine's or the outbox's reason;
    /// <see cref="AdvanceOutcome.LoadError"/> when the stored snapshot cannot be read; or
    /// <see cref="AdvanceOutcome.Conflict"/> when the instance changed while this call ran (an outcome landed, say).
    /// Nothing is written unless it is <see cref="AdvanceOutcome.Advanced"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMachine"/> is not registered or does not declare <c>SystemOwned()</c>.
    /// </exception>
    Task<AdvanceOutcome> Advance<TMachine>(
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine;
}

/// <summary>A system-owned machine instance, as <see cref="IMachineInstances.Start{TMachine}"/> returns it.</summary>
/// <param name="Id">The instance id, derived from the machine's name and the key.</param>
/// <param name="Machine">The machine's name.</param>
/// <param name="State">The state the instance is in now.</param>
/// <param name="Created">True when this call created the instance, false when it already existed.</param>
public sealed record MachineInstance(Guid Id, string Machine, string State, bool Created);

/// <summary>
/// What a system-owned instance is for: one or more string parts, such as a source and a partition. Two keys are
/// equal when their parts are equal in order. Parts are encoded so no two different keys share an encoding:
/// <c>("a|b", "c")</c> and <c>("a", "b|c")</c> are different keys with different ids.
/// </summary>
public sealed class MachineKey : IEquatable<MachineKey>
{
    // Strict: a string with an unpaired surrogate has no UTF-8 form, and the lenient encoder would replace it
    // with U+FFFD, giving two different keys one encoding.
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly string[] _parts;

    private MachineKey(string[] parts) => _parts = parts;

    /// <summary>The key's parts, in order.</summary>
    public IReadOnlyList<string> Parts => _parts;

    /// <summary>A key of one or more parts. A part may be empty; it may not be null or hold an unpaired surrogate.</summary>
    /// <param name="parts">The parts, in order.</param>
    /// <exception cref="ArgumentException">No parts, a null part, or a part with no UTF-8 encoding.</exception>
    public static MachineKey Of(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
            throw new ArgumentException("A machine key needs at least one part.", nameof(parts));
        foreach (var part in parts)
        {
            if (part is null)
                throw new ArgumentException("A machine key part cannot be null.", nameof(parts));
            try
            {
                StrictUtf8.GetByteCount(part);
            }
            catch (EncoderFallbackException ex)
            {
                throw new ArgumentException(
                    "A machine key part holds an unpaired surrogate and has no UTF-8 form.",
                    nameof(parts),
                    ex
                );
            }
        }
        return new MachineKey((string[])parts.Clone());
    }

    /// <summary>A key of one part.</summary>
    /// <param name="part">The part.</param>
    public static implicit operator MachineKey(string part) => Of(part);

    /// <summary>
    /// The canonical encoding the id is derived from: for each part in order, the decimal count of its UTF-8
    /// bytes, a colon, then the bytes. A reader that knows the format can split it back into the parts, so two
    /// different keys never share an encoding.
    /// </summary>
    internal byte[] Canonical()
    {
        using var buffer = new MemoryStream();
        foreach (var part in _parts)
        {
            var bytes = StrictUtf8.GetBytes(part);
            buffer.Write(
                Encoding.ASCII.GetBytes(
                    bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                )
            );
            buffer.Write(bytes);
        }
        return buffer.ToArray();
    }

    /// <inheritdoc/>
    public bool Equals(MachineKey? other) =>
        other is not null && _parts.AsSpan().SequenceEqual(other._parts);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as MachineKey);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in _parts)
            hash.Add(part, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>The parts, each quoted, for logs and messages.</summary>
    public override string ToString() =>
        "(" + string.Join(", ", _parts.Select(p => "\"" + p + "\"")) + ")";
}

/// <summary>
/// How a system-owned instance's id is derived, so the same key always names the same instance.
/// </summary>
/// <remarks>
/// Both steps are RFC 9562 name-based UUIDs, version 5 (SHA-1). The machine's namespace is the UUIDv5 of
/// <see cref="Root"/> and the UTF-8 bytes of the machine's name; the instance id is the UUIDv5 of that namespace and
/// the key's canonical encoding (each part as its UTF-8 byte count, a colon, and its bytes). The id therefore
/// changes if the machine is renamed, as every other draft of the machine does. These ids are fixed: changing
/// <see cref="Root"/> or the encoding would orphan every system instance already stored.
/// </remarks>
public static class MachineInstanceId
{
    /// <summary>Trax's root namespace for machine instance ids. Fixed forever.</summary>
    public static readonly Guid Root = new("0d3cd492-3485-4ca5-8fdc-e20b35ed5a85");

    /// <summary>The fixed namespace of the machine named <paramref name="machine"/>.</summary>
    /// <param name="machine">The machine's name (<see cref="IMachine.Name"/>).</param>
    public static Guid Namespace(string machine)
    {
        ArgumentException.ThrowIfNullOrEmpty(machine);
        return UuidV5(Root, Encoding.UTF8.GetBytes(machine));
    }

    /// <summary>The id of the system-owned instance of <paramref name="machine"/> for <paramref name="key"/>.</summary>
    /// <param name="machine">The machine's name (<see cref="IMachine.Name"/>).</param>
    /// <param name="key">The instance's key.</param>
    public static Guid For(string machine, MachineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return UuidV5(Namespace(machine), key.Canonical());
    }

    /// <summary>An RFC 9562 version 5 UUID of <paramref name="name"/> in <paramref name="namespaceId"/>.</summary>
    internal static Guid UuidV5(Guid namespaceId, ReadOnlySpan<byte> name)
    {
        var input = new byte[16 + name.Length];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input.AsSpan(16));

        // SHA-1 is what version 5 is defined over; this is a name derivation, not a security boundary.
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}

/// <summary>
/// Cancels the train run an invoking state queued, given the run's invoke token. A draft that holds a live token is
/// never deleted until this has returned, so draft expiry cannot strand a run. <c>AddStateMachines</c> registers the
/// implementation, which cancels as the operations surface does: a still-queued work queue entry is marked
/// cancelled, and a dispatched run has its cancel flag set, which it reads at its next junction on any host.
/// </summary>
public interface IInvokedRunCancellation
{
    /// <summary>
    /// Cancels the run whose invoke token is <paramref name="invokeToken"/>, or does nothing when it has already
    /// finished. Throw when the cancel cannot be recorded: the draft is then kept, and its run with it.
    /// </summary>
    /// <param name="invokeToken">The token the draft holds, the queued run's external id.</param>
    /// <param name="cancellationToken">
    /// The draft service passes <see cref="CancellationToken.None"/>: a request that goes away between the cancel
    /// and the delete must not leave either half done.
    /// </param>
    Task Cancel(string invokeToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a <see cref="Machine{TState,TTrigger}"/> offers this package beyond <see cref="IMachine"/>: its validated
/// initial snapshot, its ownership and invoked trains, and a draft service wired to the invoke outbox.
/// </summary>
internal interface IMachineInternals
{
    /// <summary>
    /// The machine's initial state with <paramref name="context"/> (or the declared initial context when null),
    /// validated like any snapshot, at the machine's current version.
    /// </summary>
    /// <exception cref="ArgumentException">The context is invalid for the initial state, or too large.</exception>
    Snapshot InitialSnapshot(JsonObject? context);

    /// <summary>Whether the machine declares <c>SystemOwned()</c>: only the system holds its instances.</summary>
    bool SystemOwned { get; }

    /// <summary>Every state that invokes a train, as the startup check describes it to the launcher.</summary>
    IReadOnlyList<InvokedTrainDeclaration> InvokedTrains { get; }

    /// <summary>
    /// Every outcome edge of an invoking state whose target invokes a train of its own: entering that target would
    /// queue the next run with no user present. Empty for a machine that chains no runs through outcomes.
    /// </summary>
    IReadOnlyList<ChainedOutcome> ChainedOutcomes { get; }

    /// <summary>The invoking state <paramref name="state"/> is, or null when it invokes nothing.</summary>
    EnteringInvoke? Entering(string state);

    /// <summary>Reads stored snapshot JSON, migrating it as a load does. Never throws.</summary>
    RehydrationResult Rehydrate(string json);

    /// <summary>The canonical JSON <paramref name="snapshot"/> is stored and measured as.</summary>
    string Serialize(Snapshot snapshot);

    /// <summary>Applies an invoked run's <paramref name="outcome"/> to <paramref name="snapshot"/>. Never throws.</summary>
    AdvanceResult ApplyOutcome(Snapshot snapshot, InvokeOutcome outcome);

    /// <summary>Fires <paramref name="trigger"/> on <paramref name="snapshot"/> through the engine. Never throws.</summary>
    AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input);

    /// <summary>Whether <paramref name="trigger"/> is an invoked train's outcome trigger, which only its run applies.</summary>
    bool IsOutcomeTrigger(string trigger);

    /// <summary>Whether <paramref name="trigger"/> from <paramref name="state"/> runs the machine's effect.</summary>
    bool IsEffectBound(string state, string trigger);

    /// <summary>
    /// <see cref="IMachine.CreateService"/>, with the cancellation a draft deletion calls first, and the runtime a
    /// machine that invokes trains writes through (null for one that invokes none).
    /// </summary>
    ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl,
        IInvokedRunCancellation? runCancellation,
        InvokeRuntime? invokes
    );
}

/// <summary>An outcome edge of an invoking state that enters another invoking state.</summary>
/// <param name="State">The invoking state the run belongs to.</param>
/// <param name="Outcome">The outcome, <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c>.</param>
/// <param name="Target">The state the outcome enters, which invokes a train of its own.</param>
internal sealed record ChainedOutcome(string State, string Outcome, string Target);

/// <summary>
/// What a draft service of a machine that invokes trains writes through: the owner-aware store over the request's
/// data context, and the outbox that writes a snapshot together with the runs it queues or cancels.
/// </summary>
/// <param name="Store">The owner-aware store the draft service reads and writes, over the request's data context.</param>
/// <param name="Outbox">Writes a snapshot entering or leaving an invoking state in one transaction.</param>
internal sealed record InvokeRuntime(IMachineInstanceStore Store, InvokeOutbox Outbox);

/// <summary>The default <see cref="IMachineInstances"/>, registered scoped by <c>AddStateMachines</c>.</summary>
internal sealed class MachineInstances(
    IEnumerable<IMachine> machines,
    IMachineInstanceStore store,
    InvokeOutbox outbox
) : IMachineInstances
{
    public async Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine
    {
        ArgumentNullException.ThrowIfNull(key);

        var (machine, internals) = SystemMachine<TMachine>("start");
        var name = machine.Name;
        var id = MachineInstanceId.For(name, key);

        if (await store.Get(DraftOwner.System, name, id, cancellationToken) is { } existing)
            return Existing(id, name, existing);

        var initial = internals.InitialSnapshot(context);

        // Entering an invoking initial state queues its run and sets the row's invoke token, in the transaction
        // that inserts the row, so a lost race to create the instance queues nothing.
        var created = await outbox.Insert(
            DraftOwner.System,
            id,
            initial,
            internals.Entering(initial.State),
            cancellationToken
        );
        switch (created)
        {
            case InvokeWrite.Written:
                return new MachineInstance(id, name, initial.State, Created: true);
            case InvokeWrite.Refused refused:
                throw new InvalidOperationException(
                    $"The system instance {id} of '{name}' could not be started ({refused.Code}): {refused.Message}",
                    refused.Exception
                );
        }

        // Another Start for the same key created the row between the read and the insert: return that one.
        return await store.Get(DraftOwner.System, name, id, cancellationToken) is { } raced
            ? Existing(id, name, raced)
            : throw new InvalidOperationException(
                $"The system instance {id} of '{name}' could not be created, and none exists."
            );
    }

    public async Task<AdvanceOutcome> Advance<TMachine>(
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var (machine, internals) = SystemMachine<TMachine>("advance");
        var name = machine.Name;
        var id = MachineInstanceId.For(name, key);

        var stored = await store.Get(DraftOwner.System, name, id, cancellationToken);
        if (stored is null)
            return new AdvanceOutcome.NotFound();

        Snapshot current;
        switch (internals.Rehydrate(stored.Json))
        {
            case RehydrationResult.Ok ok:
                current = ok.Snapshot;
                break;
            case RehydrationResult.Error error:
                return new AdvanceOutcome.LoadError(error.Code, error.Message)
                {
                    Exception = error.Exception,
                };
            default:
                return new AdvanceOutcome.LoadError(
                    RehydrationErrorCodes.Malformed,
                    "Unknown rehydration result."
                );
        }

        // The same reserved triggers a user's advance refuses: the system may move its instance, but it may not
        // forge what a run or an effect produced.
        if (internals.IsEffectBound(current.State, trigger))
            return new AdvanceOutcome.Rejected(
                "effect-bound",
                "This action runs an irreversible effect, which only its send fires."
            );
        if (internals.IsOutcomeTrigger(trigger))
            return new AdvanceOutcome.Rejected(
                "outcome-bound",
                "This step is completed by the outcome of its work, not by an action."
            );

        Snapshot next;
        switch (internals.Advance(current, trigger, input))
        {
            case AdvanceResult.Rejected rejected:
                return new AdvanceOutcome.Rejected(rejected.Reason, rejected.Detail)
                {
                    Exception = rejected.Exception,
                };
            case AdvanceResult.Transitioned transitioned:
                next = transitioned.Snapshot;
                break;
            default:
                return new AdvanceOutcome.Rejected(RejectionReasons.InternalError, null);
        }

        if (StorableJson.Problem(next.Context) is { } unstorable)
            return new AdvanceOutcome.Rejected(RehydrationErrorCodes.Malformed, unstorable);
        if (Encoding.UTF8.GetByteCount(internals.Serialize(next)) > SnapshotLimits.MaxSnapshotBytes)
            return new AdvanceOutcome.Rejected(
                "too-large",
                $"The advanced snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        // Leaving an invoking state cancels its run and entering one queues a new run, with the snapshot, in one
        // transaction. A self-loop neither leaves nor enters, and the state keeps its run.
        var moved = next.State != current.State;
        var write = await outbox.Advance(
            DraftOwner.System,
            id,
            next,
            stored.Token,
            request: null,
            moved && internals.Entering(current.State) is not null ? stored.InvokeToken : null,
            moved ? internals.Entering(next.State) : null,
            cancellationToken
        );
        return write switch
        {
            InvokeWrite.Written => new AdvanceOutcome.Advanced(next),
            InvokeWrite.Refused refused => new AdvanceOutcome.Rejected(
                refused.Code,
                refused.Message
            )
            {
                Exception = refused.Exception,
            },
            _ => new AdvanceOutcome.Conflict(),
        };
    }

    // The registered, system-owned machine TMachine names, or why there is none.
    private (IMachine Machine, IMachineInternals Internals) SystemMachine<TMachine>(string action)
        where TMachine : IMachine
    {
        var machine =
            machines.OfType<TMachine>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{typeof(TMachine).Name} is not registered. Pass its assembly to AddStateMachines before "
                    + $"you {action} an instance of it."
            );
        if (machine is not IMachineInternals internals)
            throw new InvalidOperationException(
                $"{typeof(TMachine).Name} does not derive from Machine<TState, TTrigger>, so the system cannot "
                    + $"{action} an instance of it."
            );
        if (!internals.SystemOwned)
            throw new InvalidOperationException(
                $"{typeof(TMachine).Name} ('{machine.Name}') is a user-owned machine, so the system cannot "
                    + $"{action} an instance of it. Declare SystemOwned() in its Configure for its instances to "
                    + "belong to the system; its users then reach none of them."
            );
        return (machine, internals);
    }

    private static MachineInstance Existing(Guid id, string machine, StoredSnapshot stored) =>
        new(
            id,
            machine,
            JsonNode.Parse(stored.Json)?["state"]?.GetValue<string>() ?? string.Empty,
            Created: false
        );
}
