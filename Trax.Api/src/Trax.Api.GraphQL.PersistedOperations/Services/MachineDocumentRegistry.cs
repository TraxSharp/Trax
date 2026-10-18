using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>What the store holds for one machine document id: the version it registered under, and whether it is active.</summary>
internal readonly record struct StoredMachineDocument(int Version, bool IsActive);

/// <summary>What a node does with a request naming a machine document by id.</summary>
internal enum MachineDocumentVerdict
{
    /// <summary>One of this node's own documents: look it up and run it.</summary>
    Own,

    /// <summary>A document of an older version of the machine, or one no version generated: the client reloads.</summary>
    ClientOutdated,

    /// <summary>
    /// A document of a newer version of the machine than this node runs, or one of a machine this node does not serve
    /// that the store holds active: the client retries.
    /// </summary>
    ServerOutdated,

    /// <summary>
    /// A client document another node registered that the store holds active: it runs here if its stored text passes
    /// this node's checks, and answers <see cref="ClientOutdated"/> if not.
    /// </summary>
    Stored,
}

/// <summary>
/// This node's machine documents, and what it knows of the others stored beside them: the order in which each version
/// of a machine registered its documents. A node serves only its own documents; any other id under the reserved
/// prefix is answered by comparing its version with this node's, never run.
/// </summary>
/// <remarks>
/// <para>
/// Each set of documents a node registers for a machine is numbered when it first registers, one higher than any
/// before it (the <c>version</c> column), so an id stored before this node's documents is older and one stored after
/// is newer, whatever the clocks say.
/// </para>
/// <para>
/// A stored id of a machine this node does not serve is a newer node's (a machine exposed since this node was built)
/// or an older one's (a machine a newer deploy stopped exposing): while the store holds it active a node may serve it,
/// so it answers <see cref="MachineDocumentVerdict.ServerOutdated"/>; once it was collected, which happens a grace
/// period after no registering node serves its machine, it answers <see cref="MachineDocumentVerdict.ClientOutdated"/>.
/// </para>
/// <para>
/// An id this node has not seen, of any machine, is answered at once from the last read of the store, never by
/// waiting for one: the store is read again in the background, at most once per <see cref="RefreshInterval"/> however
/// many such requests arrive. Until a read that began after the id was first asked for has finished, the id may be a
/// newer node's, so it answers <see cref="MachineDocumentVerdict.ServerOutdated"/>; once such a read does not hold it,
/// no version generated it, or it was collected, and it answers <see cref="MachineDocumentVerdict.ClientOutdated"/>.
/// The ids waiting for a read are remembered up to <see cref="MissCapacity"/>, the oldest forgotten first. An id that
/// is not shaped like a generated one costs no read at all.
/// </para>
/// <para>See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </remarks>
internal sealed class MachineDocumentRegistry : IMachineIrOrder
{
    /// <summary>The default shortest time between two reads of the stored versions that requests can cause.</summary>
    internal static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many unseen ids are remembered while they wait for a read; past it the oldest are forgotten first.</summary>
    internal const int MissCapacity = 4096;

    private readonly ConcurrentDictionary<string, long> _misses = new(StringComparer.Ordinal);

    // What this node decided about each stored client document it was asked for: whether its stored text passes this
    // node's checks. Only ids the store holds active get here, so it holds no more than the store does.
    private readonly ConcurrentDictionary<string, bool> _storedChecks = new(StringComparer.Ordinal);

    // Every id remembered in _misses, with the time it was first asked for, oldest first. An entry whose id was
    // answered since, or remembered again later, is left behind and skipped when it is dequeued; the queue is trimmed
    // to MissCapacity, so neither collection outgrows it.
    private readonly ConcurrentQueue<(string Id, long FirstAsked)> _missOrder = new();
    private volatile State? _state;
    private volatile Func<
        CancellationToken,
        Task<IReadOnlyDictionary<string, StoredMachineDocument>>
    >? _readStored;
    private int _refreshing;
    private int _wanted;

    /// <summary>The shortest time between two reads of the stored versions that requests can cause.</summary>
    internal TimeSpan RefreshInterval { get; init; } = MinRefreshInterval;

    /// <summary>
    /// What a client document may reach on this node, set when the node registers its documents. Until then no
    /// stored client document passes.
    /// </summary>
    public ClientDocumentReach ClientReach { get; set; } = ClientDocumentReach.None;

    /// <summary>Whether this node has registered its documents.</summary>
    public bool IsReady => _state is not null;

    /// <summary>This node's own documents, in the order <see cref="MachineDocuments.Generate"/> returned them.</summary>
    public IReadOnlyList<GeneratedDocument> Documents => _state?.Documents ?? [];

    /// <summary>This node's document under <paramref name="id"/>, if it is one of its own.</summary>
    public bool TryGetOwn(string id, out GeneratedDocument document)
    {
        if (_state is { } state && state.Own.TryGetValue(id, out var own))
        {
            document = own;
            return true;
        }
        document = null!;
        return false;
    }

    /// <summary>
    /// Records the documents this node registered, its version of each machine, and every stored id's version, as read
    /// by a read of the store that began at <paramref name="readStartedAt"/> (a <see cref="Stopwatch"/> timestamp; now
    /// when omitted).
    /// </summary>
    public void Publish(
        IReadOnlyList<GeneratedDocument> documents,
        IReadOnlyDictionary<string, int> ownVersions,
        IReadOnlyDictionary<string, StoredMachineDocument> stored,
        long? readStartedAt = null
    )
    {
        var readAt = readStartedAt ?? Stopwatch.GetTimestamp();
        _state = new State(
            documents,
            documents.ToDictionary(d => d.Id, StringComparer.Ordinal),
            ownVersions,
            stored,
            readAt,
            Stopwatch.GetTimestamp()
        );
    }

    /// <summary>
    /// What to do with a request naming <paramref name="id"/>, which is under the reserved prefix. Never waits on the
    /// store: <paramref name="readStored"/> reads every stored id's version and state, and is called in the background,
    /// for a well-formed id this node has not seen, at most once per <see cref="RefreshInterval"/>.
    /// </summary>
    public ValueTask<MachineDocumentVerdict> ClassifyAsync(
        string id,
        Func<
            CancellationToken,
            Task<IReadOnlyDictionary<string, StoredMachineDocument>>
        > readStored,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(Classify(id, readStored));

    private MachineDocumentVerdict Classify(
        string id,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, StoredMachineDocument>>> readStored
    )
    {
        if (_state is not { } state)
            // Not registered yet: a newer node's documents and this node's own look alike until it has, so the
            // client is told to retry, which nothing caches.
            return MachineDocumentVerdict.ServerOutdated;
        if (state.Own.ContainsKey(id))
            return MachineDocumentVerdict.Own;
        if (!MachineDocuments.IsWellFormed(id))
            return MachineDocumentVerdict.ClientOutdated;

        var machine = GeneratedDocuments.SlotOf(id, state.OwnVersions.Keys);
        if (state.Stored.TryGetValue(id, out var stored))
        {
            _misses.TryRemove(id, out _);
            // A client or console document runs on any node while the store holds it active, whatever its version:
            // the text another node registered still names fields this node serves, or this node's checks refuse it.
            // An older version keeps working through a frontend's deploy; a collected one is reloaded.
            if (GeneratedDocuments.IsContentAddressed(id))
                return stored.IsActive
                    ? MachineDocumentVerdict.Stored
                    : MachineDocumentVerdict.ClientOutdated;
            // A machine this node does not serve: a node may serve the id while the store holds it active. A newer
            // deploy that stopped exposing the machine has it collected after the grace period, and then its clients
            // reload.
            if (machine is null)
                return stored.IsActive
                    ? MachineDocumentVerdict.ServerOutdated
                    : MachineDocumentVerdict.ClientOutdated;
            // A newer version is retried while it is active. Once collected no node serves it any more (a rollback),
            // so its clients reload.
            return stored.IsActive && stored.Version > state.OwnVersions[machine]
                ? MachineDocumentVerdict.ServerOutdated
                : MachineDocumentVerdict.ClientOutdated;
        }

        // Not in the last read. If that read began after the id was first asked for, the store did not hold it then.
        if (_misses.TryGetValue(id, out var firstAsked) && state.ReadStartedAt > firstAsked)
        {
            _misses.TryRemove(id, out _);
            return MachineDocumentVerdict.ClientOutdated;
        }

        Remember(id);
        RequestRefresh(readStored);
        return MachineDocumentVerdict.ServerOutdated;
    }

    // Remembers when an unseen id was first asked for, forgetting the oldest remembered ids past MissCapacity: a flood
    // of made-up ids pushes out only ids older than itself, never every id at once, so a real stale client asked
    // about within the interval still turns client-outdated after the next read.
    private void Remember(string id)
    {
        var firstAsked = Stopwatch.GetTimestamp();
        if (!_misses.TryAdd(id, firstAsked))
            return;
        _missOrder.Enqueue((id, firstAsked));
        while (_missOrder.Count > MissCapacity && _missOrder.TryDequeue(out var oldest))
            _misses.TryRemove(KeyValuePair.Create(oldest.Id, oldest.FirstAsked));
    }

    /// <summary>
    /// What this node decided about the stored client document <paramref name="id"/>: whether it passes this node's
    /// checks, or null when it was not checked yet.
    /// </summary>
    public bool? StoredCheck(string id) =>
        _storedChecks.TryGetValue(id, out var passes) ? passes : null;

    /// <summary>Records whether the stored client document <paramref name="id"/> passes this node's checks.</summary>
    public void RecordStoredCheck(string id, bool passes) => _storedChecks[id] = passes;

    /// <summary>
    /// Whether <paramref name="id"/> is a stored client document this node checked and may run: the store held it
    /// active at the last read, and its text passed.
    /// </summary>
    public bool IsRunnableStored(string id) =>
        _state is { } state
        && state.Stored.TryGetValue(id, out var stored)
        && stored.IsActive
        && StoredCheck(id) == true;

    /// <summary>How many unseen ids are remembered now, for the tests of the bound.</summary>
    internal int RememberedMisses => _misses.Count;

    /// <summary>Whether <paramref name="id"/> is remembered as unseen, for the tests of the eviction order.</summary>
    internal bool IsRemembered(string id) => _misses.ContainsKey(id);

    /// <inheritdoc/>
    /// <remarks>
    /// Read from what this node knows of the stored documents, never the store itself: an IR hash is as new as the
    /// version its documents registered under. A hash none of them carries was registered after this node last read
    /// the store (or never), and is answered as newer, so an older node's stream ends rather than follow a row it may
    /// not understand.
    /// </remarks>
    public bool? IsNewer(string machine, string irHash)
    {
        if (_state is not { } state || !state.OwnVersions.TryGetValue(machine, out var own))
            return null;
        if (
            state.Documents.Any(d =>
                d is MachineDocument m
                && m.Machine == machine
                && string.Equals(m.IrHash, irHash, StringComparison.Ordinal)
            )
        )
            return false;

        int? version = null;
        foreach (var (id, stored) in state.Stored)
            if (
                MachineDocuments.MachineOf(id, [machine]) is not null
                && MachineDocuments.IrHashOf(id) is { } hash
                && string.Equals(hash, irHash, StringComparison.Ordinal)
            )
                version = Math.Max(version ?? stored.Version, stored.Version);
        return version is not { } found || found > own;
    }

    // Asks for a read of the store. One runs at a time, in the background; a request made while one runs is served by
    // the next, which starts no sooner than the interval after the last one finished.
    private void RequestRefresh(
        Func<CancellationToken, Task<IReadOnlyDictionary<string, StoredMachineDocument>>> readStored
    )
    {
        _readStored = readStored;
        Volatile.Write(ref _wanted, 1);
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            _ = Task.Run(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _wanted, 0) == 1)
            {
                if (_state is not { } before || _readStored is not { } read)
                    return;
                var since = Stopwatch.GetElapsedTime(before.ReadFinishedAt);
                if (since < RefreshInterval)
                    await Task.Delay(RefreshInterval - since).ConfigureAwait(false);

                var startedAt = Stopwatch.GetTimestamp();
                IReadOnlyDictionary<string, StoredMachineDocument> stored;
                try
                {
                    stored = await read(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The store could not be read: the ids stay unseen, answered as retryable, and the next miss asks
                    // again no sooner than the interval.
                    _state = _state! with
                    {
                        ReadFinishedAt = Stopwatch.GetTimestamp(),
                    };
                    continue;
                }
                _state = _state! with
                {
                    Stored = stored,
                    ReadStartedAt = startedAt,
                    ReadFinishedAt = Stopwatch.GetTimestamp(),
                };
            }
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
            // A miss that arrived after the loop's last check, while the flag was still held, asks again.
            if (Volatile.Read(ref _wanted) == 1 && _readStored is { } again)
                RequestRefresh(again);
        }
    }

    private sealed record State(
        IReadOnlyList<GeneratedDocument> Documents,
        IReadOnlyDictionary<string, GeneratedDocument> Own,
        IReadOnlyDictionary<string, int> OwnVersions,
        IReadOnlyDictionary<string, StoredMachineDocument> Stored,
        long ReadStartedAt,
        long ReadFinishedAt
    );
}

/// <summary>
/// The instruments that alert on a machine document whose stored body does not match its id: a lookup refused, and a
/// row startup wrote back.
/// </summary>
internal static class MachineDocumentInstruments
{
    /// <summary>The meter both instruments belong to.</summary>
    public const string MeterName = "Trax.Api.PersistedOperations";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>A lookup refused because the stored body does not hash to its id.</summary>
    public static readonly Counter<long> Mismatches = Meter.CreateCounter<long>(
        "trax.persisted_operations.machine_document_mismatches",
        description: "Lookups of a machine document refused because the stored body does not match its id."
    );

    /// <summary>A stored machine document startup wrote back because its body did not match its id.</summary>
    public static readonly Counter<long> Repairs = Meter.CreateCounter<long>(
        "trax.persisted_operations.machine_document_repairs",
        description: "Machine documents written back at startup because the stored body did not match its id."
    );
}
