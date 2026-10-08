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
[Experimental(ExperimentalIds.Invokes)]
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
    /// <exception cref="InvalidOperationException"><typeparamref name="TMachine"/> is not registered.</exception>
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
}

/// <summary>A system-owned machine instance, as <see cref="IMachineInstances.Start{TMachine}"/> returns it.</summary>
/// <param name="Id">The instance id, derived from the machine's name and the key.</param>
/// <param name="Machine">The machine's name.</param>
/// <param name="State">The state the instance is in now.</param>
/// <param name="Created">True when this call created the instance, false when it already existed.</param>
[Experimental(ExperimentalIds.Invokes)]
public sealed record MachineInstance(Guid Id, string Machine, string State, bool Created);

/// <summary>
/// What a system-owned instance is for: one or more string parts, such as a source and a partition. Two keys are
/// equal when their parts are equal in order. Parts are encoded so no two different keys share an encoding:
/// <c>("a|b", "c")</c> and <c>("a", "b|c")</c> are different keys with different ids.
/// </summary>
[Experimental(ExperimentalIds.Invokes)]
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
[Experimental(ExperimentalIds.Invokes)]
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
/// never deleted until this has returned, so draft expiry cannot strand a run. The package that launches invoked
/// trains registers the implementation; until one is registered the default does nothing, because no state can have
/// queued a run.
/// </summary>
[Experimental(ExperimentalIds.Invokes)]
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

/// <summary>The default <see cref="IInvokedRunCancellation"/> before a launcher registers one: nothing to cancel.</summary>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class NoInvokedRunCancellation : IInvokedRunCancellation
{
    public Task Cancel(string invokeToken, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// What a <see cref="Machine{TState,TTrigger}"/> offers this package beyond <see cref="IMachine"/>: its validated
/// initial snapshot, and a draft service that cancels a live invoked run before it deletes a draft.
/// </summary>
internal interface IMachineInternals
{
    /// <summary>
    /// The machine's initial state with <paramref name="context"/> (or the declared initial context when null),
    /// validated like any snapshot, at the machine's current version.
    /// </summary>
    /// <exception cref="ArgumentException">The context is invalid for the initial state, or too large.</exception>
    Snapshot InitialSnapshot(JsonObject? context);

    /// <summary><see cref="IMachine.CreateService"/>, with the cancellation a draft deletion calls first.</summary>
    ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl,
        IInvokedRunCancellation? runCancellation
    );
}

/// <summary>The default <see cref="IMachineInstances"/>, registered scoped by <c>AddStateMachines</c>.</summary>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class MachineInstances(IEnumerable<IMachine> machines, IMachineInstanceStore store)
    : IMachineInstances
{
    public async Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine
    {
        ArgumentNullException.ThrowIfNull(key);

        var machine =
            machines.OfType<TMachine>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{typeof(TMachine).Name} is not registered. Pass its assembly to AddStateMachines before "
                    + "starting an instance of it."
            );
        if (machine is not IMachineInternals internals)
            throw new InvalidOperationException(
                $"{typeof(TMachine).Name} does not derive from Machine<TState, TTrigger>, so it has no initial "
                    + "snapshot to start an instance from."
            );

        var name = machine.Name;
        var id = MachineInstanceId.For(name, key);

        if (await store.Get(DraftOwner.System, name, id, cancellationToken) is { } existing)
            return Existing(id, name, existing);

        var initial = internals.InitialSnapshot(context);
        if (await store.Insert(DraftOwner.System, id, initial, cancellationToken))
        {
            await EnterInitialState(name, id, initial, cancellationToken);
            return new MachineInstance(id, name, initial.State, Created: true);
        }

        // Another Start for the same key created the row between the read and the insert: return that one.
        return await store.Get(DraftOwner.System, name, id, cancellationToken) is { } raced
            ? Existing(id, name, raced)
            : throw new InvalidOperationException(
                $"The system instance {id} of '{name}' could not be created, and none exists."
            );
    }

    // Entering an invoking initial state queues its run and sets the row's invoke token, in the transaction that
    // inserts the row. Nothing is queued yet: this is where the launcher hooks in.
    private static Task EnterInitialState(
        string machine,
        Guid id,
        Snapshot initial,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    private static MachineInstance Existing(Guid id, string machine, StoredSnapshot stored) =>
        new(
            id,
            machine,
            JsonNode.Parse(stored.Json)?["state"]?.GetValue<string>() ?? string.Empty,
            Created: false
        );
}
