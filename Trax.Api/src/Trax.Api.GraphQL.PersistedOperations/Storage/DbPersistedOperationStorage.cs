using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.ShapeDiff;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;
using Trax.Effect.Models.PersistedOperationHistory;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// EF Core-backed implementation of both <see cref="IPersistedOperationStore"/>
/// (programmatic CRUD) and <see cref="IOperationDocumentStorage"/> (the
/// HotChocolate hot-path used by the request executor). Reads and writes
/// against the existing Trax <c>IDataContext</c>; no dedicated DbContext.
/// </summary>
internal sealed class DbPersistedOperationStorage
    : IPersistedOperationStore,
        IOperationDocumentStorage
{
    /// <summary>
    /// Sentinel used by the schema for "no tenant". The PK is composite over
    /// <c>(tenant_key, id)</c>; Postgres disallows nulls in PK columns, so we
    /// store '' and translate at the C# boundary.
    /// </summary>
    internal const string NoTenantSentinel = "";

    private readonly IDataContextProviderFactory _factory;
    private readonly PersistedOperationsOptions _options;
    private readonly IPersistedOperationCache _cache;
    private readonly IPersistedOperationBroadcaster _broadcaster;
    private readonly IPersistedOperationValidator _validator;
    private readonly HotChocolateOperationCacheInvalidator _hcInvalidator;
    private readonly PersistedOperationCacheGeneration _generation;
    private readonly StampedCache<string> _misses;
    private readonly TimeProvider _clock;
    private readonly ILogger<DbPersistedOperationStorage> _logger;
    private readonly MachineDocumentRegistry _machineDocuments;

    /// <summary>
    /// How many unknown ids each generation of the miss cache holds; the cache keeps at most
    /// twice this.
    /// </summary>
    internal const int MissCacheCapacity = 512;

    public DbPersistedOperationStorage(
        IDataContextProviderFactory factory,
        PersistedOperationsOptions options,
        IPersistedOperationCache cache,
        IPersistedOperationBroadcaster broadcaster,
        IPersistedOperationValidator validator,
        HotChocolateOperationCacheInvalidator hcInvalidator,
        PersistedOperationCacheGeneration generation,
        TimeProvider clock,
        ILogger<DbPersistedOperationStorage> logger,
        MachineDocumentRegistry? machineDocuments = null
    )
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(hcInvalidator);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _options = options;
        _cache = cache;
        _broadcaster = broadcaster;
        _validator = validator;
        _hcInvalidator = hcInvalidator;
        _generation = generation;
        _misses = new StampedCache<string>(
            MissCacheCapacity,
            generation,
            clock,
            options.CacheMaxAge
        );
        _clock = clock;
        _logger = logger;
        _machineDocuments = machineDocuments ?? new MachineDocumentRegistry();
    }

    // ----- IOperationDocumentStorage (HC hot path) -----

    /// <summary>
    /// Reads the active document stored under <paramref name="documentId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that sent its own document is never looked up: HotChocolate names such a request
    /// by its document's hash and asks the store for it, but the document runs as sent, so the
    /// store has nothing to add, and under <c>RequirePersisted</c> the document is refused without
    /// a database read.
    /// </para>
    /// <para>
    /// An id the store does not hold is remembered, so asking for it again does not read the
    /// database until the next change or the cache's maximum age. Every entry, found or missing,
    /// is stamped with the generation that was current before the read, so a read a change
    /// overtook is never served. See
    /// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
    /// </para>
    /// </remarks>
    public async ValueTask<IOperationDocument?> TryReadAsync(
        OperationDocumentId documentId,
        CancellationToken cancellationToken
    )
    {
        if (documentId.IsEmpty)
            return null;

        var scope = PersistedOperationRequestScope.For(_generation);
        if (scope is { CarriesDocument: true })
            return null;

        var id = documentId.Value;
        // v1 has no tenant resolver; hot-path lookups always use the null-tenant row set.
        var tenantKey = (string?)null;

        // Both taken before anything is read, so whatever the read returns is filed under a
        // generation and time no later than the data it saw.
        var generation = _generation.Current;
        var readAt = _clock.GetTimestamp();

        if (_cache is InMemoryPersistedOperationCache stamped)
        {
            if (stamped.TryGet(tenantKey, id, out var hit, out var hitReadAt))
            {
                scope?.NoteSource(hitReadAt);
                return new OperationDocumentSourceText(hit);
            }
        }
        else if (_cache.TryGet(tenantKey, id) is { } cached)
        {
            return new OperationDocumentSourceText(cached);
        }

        // An id under the reserved prefix is a machine document: only this node's own are read, never one it did
        // not generate, and a miss is not remembered, so the ids a client invents cannot fill the miss cache.
        GeneratedDocument? own = null;
        var storedClient = false;
        if (GeneratedDocuments.IsReserved(id))
        {
            if (_machineDocuments.TryGetOwn(id, out var generated))
                own = generated;
            // Another node's client or console document, which the middleware let through only once this node checked
            // and validated it: read, and served only when its body is the one its id names.
            else if (
                GeneratedDocuments.IsContentAddressed(id) && _machineDocuments.IsRunnableStored(id)
            )
                storedClient = true;
            else
                return null;
        }
        else if (_misses.TryGet(id, out _))
            return null;

        var sentinel = Normalize(tenantKey);
        var ctx = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ctx
                .PersistedOperations.AsNoTracking()
                .Where(p => p.TenantKey == sentinel && p.Id == id && p.IsActive)
                .Select(p => p.Document)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                if (own is null && !storedClient)
                    _misses.Add(id, id, generation, readAt);
                return null;
            }

            if (storedClient && !GeneratedDocuments.IsGenerated(id, document))
                return null;

            if (own is not null && !own.Matches(document))
            {
                MachineDocumentInstruments.Mismatches.Add(
                    1,
                    new KeyValuePair<string, object?>("slot", own.Slot)
                );
                _logger.LogError(
                    "The stored body of generated document '{Id}' does not match its id, so it is refused. A restart "
                        + "of this node writes the generated body back; until then, find who changed the row.",
                    id
                );
                return null;
            }

            if (_cache is InMemoryPersistedOperationCache stampedCache)
                stampedCache.Set(tenantKey, id, document, generation, readAt);
            else if (_generation.Current == generation)
                // A cache Trax did not supply cannot carry the stamp; skip it when a change
                // overtook the read.
                _cache.Set(tenantKey, id, document);

            scope?.NoteSource(readAt);
            return new OperationDocumentSourceText(document);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    public ValueTask SaveAsync(
        OperationDocumentId documentId,
        IOperationDocument document,
        CancellationToken cancellationToken
    ) =>
        // Trax's persisted-operation lifecycle is operator-managed (admin
        // tooling / IPersistedOperationStore.UpsertAsync), never via
        // HotChocolate's automatic-persisted-queries fallback.
        throw new NotSupportedException(
            "Trax persisted operations are operator-managed. Use IPersistedOperationStore.UpsertAsync from admin tooling instead."
        );

    // ----- IPersistedOperationStore -----

    public async Task<PersistedOperation?> GetAsync(
        string id,
        string? tenantKey,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var sentinel = Normalize(tenantKey);

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            var row = await ctx
                .PersistedOperations.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantKey == sentinel && p.Id == id && p.IsActive, ct)
                .ConfigureAwait(false);

            return row is null ? null : Denormalize(row);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PersistedOperation>> ListAsync(
        string? tenantKey,
        CancellationToken ct
    )
    {
        var sentinel = Normalize(tenantKey);

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = await ctx
                .PersistedOperations.AsNoTracking()
                .Where(p => p.TenantKey == sentinel)
                .OrderBy(p => p.OperationName)
                .ThenBy(p => p.Version)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var row in rows)
                row.TenantKey = string.IsNullOrEmpty(row.TenantKey) ? null : row.TenantKey;

            return rows;
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    public async Task<PersistedOperation> UpsertAsync(
        string id,
        string document,
        UpsertOptions? options,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(document);
        RefuseReserved(id);

        // Validate against the live schema before any DB work; the validator
        // throws structured exceptions that callers project into form errors
        // or GraphQL error payloads. No row is written and no broadcast fires
        // if validation fails.
        await _validator.ValidateAsync(document, ct).ConfigureAwait(false);
        RequireExactlyOneOperation(document);

        // OperationName is taken from the document's operation definition
        // (the GraphQL spec sense). The id is opaque — no parse rule.
        // Version is operator-controlled metadata via UpsertOptions.
        var operationName = ExtractOperationName(document);
        var version = options?.Version ?? 0;
        // Convention: each persisted document holds exactly one operation, so
        // the fingerprint computer disambiguates by "the only operation".
        var fingerprint = ShapeFingerprintComputer.Compute(document);
        var tenantKey = options?.TenantKey;
        var sentinel = Normalize(tenantKey);
        var now = _clock.GetUtcNow().UtcDateTime;

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            await using var tx = await LockAsync(ctx, sentinel, id, ct).ConfigureAwait(false);

            var existing = await ctx
                .PersistedOperations.FirstOrDefaultAsync(
                    p => p.TenantKey == sentinel && p.Id == id,
                    ct
                )
                .ConfigureAwait(false);

            if (existing is null)
            {
                existing = new PersistedOperation
                {
                    TenantKey = sentinel,
                    Id = id,
                    OperationName = operationName,
                    Version = version,
                    Document = document,
                    ShapeFingerprint = fingerprint,
                    IsActive = true,
                    Description = options?.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                ctx.PersistedOperations.Add(existing);
            }
            else
            {
                // Shape-diff guardrail: an edit that changes the response
                // shape would silently break every shipped client that is
                // reading by the same id. Reject unless BypassShapeDiff is
                // set (the operator's documented escape hatch for cases
                // where they have verified the change is shape-safe).
                if (
                    !string.Equals(existing.ShapeFingerprint, fingerprint, StringComparison.Ordinal)
                    && options?.BypassShapeDiff != true
                )
                {
                    throw new ShapeDiffViolationException(
                        id,
                        existing.ShapeFingerprint,
                        fingerprint
                    );
                }

                existing.OperationName = operationName;
                existing.Version = version;
                existing.Document = document;
                existing.ShapeFingerprint = fingerprint;
                existing.IsActive = true;
                existing.DeprecationReason = null;
                if (options?.Description is { } d)
                    existing.Description = d;
                existing.UpdatedAt = now;
            }

            ctx.PersistedOperationHistories.Add(
                new PersistedOperationHistory
                {
                    TenantKey = sentinel,
                    Id = id,
                    Document = document,
                    ShapeFingerprint = fingerprint,
                    ChangeType = PersistedOperationChangeType.Upsert,
                    ChangedAt = now,
                    ChangedReason = options?.Description,
                }
            );

            await ctx.SaveChanges(ct).ConfigureAwait(false);
            if (tx is not null)
                await tx.CommitAsync(ct).ConfigureAwait(false);

            var saved = Denormalize(existing);
            await ApplyChangeAsync(tenantKey, id, PersistedOperationChangeType.Upsert, saved)
                .ConfigureAwait(false);
            return saved;
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    public async Task DeactivateAsync(
        string id,
        string? tenantKey,
        string reason,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(reason);
        RefuseReserved(id);
        var sentinel = Normalize(tenantKey);
        var now = _clock.GetUtcNow().UtcDateTime;

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            await using var tx = await LockAsync(ctx, sentinel, id, ct).ConfigureAwait(false);

            var row =
                await ctx
                    .PersistedOperations.FirstOrDefaultAsync(
                        p => p.TenantKey == sentinel && p.Id == id,
                        ct
                    )
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Persisted operation '{id}' not found for tenant '{tenantKey ?? "(none)"}'."
                );

            row.IsActive = false;
            row.DeprecationReason = reason;
            row.UpdatedAt = now;

            ctx.PersistedOperationHistories.Add(
                new PersistedOperationHistory
                {
                    TenantKey = sentinel,
                    Id = id,
                    Document = row.Document,
                    ShapeFingerprint = row.ShapeFingerprint,
                    ChangeType = PersistedOperationChangeType.Deactivate,
                    ChangedAt = now,
                    ChangedReason = reason,
                }
            );

            await ctx.SaveChanges(ct).ConfigureAwait(false);
            if (tx is not null)
                await tx.CommitAsync(ct).ConfigureAwait(false);

            await ApplyChangeAsync(tenantKey, id, PersistedOperationChangeType.Deactivate, null)
                .ConfigureAwait(false);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    public async Task RestoreAsync(string id, string? tenantKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        RefuseReserved(id);
        var sentinel = Normalize(tenantKey);
        var now = _clock.GetUtcNow().UtcDateTime;

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            await using var tx = await LockAsync(ctx, sentinel, id, ct).ConfigureAwait(false);

            var row =
                await ctx
                    .PersistedOperations.FirstOrDefaultAsync(
                        p => p.TenantKey == sentinel && p.Id == id,
                        ct
                    )
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Persisted operation '{id}' not found for tenant '{tenantKey ?? "(none)"}'."
                );

            row.IsActive = true;
            row.DeprecationReason = null;
            row.UpdatedAt = now;

            ctx.PersistedOperationHistories.Add(
                new PersistedOperationHistory
                {
                    TenantKey = sentinel,
                    Id = id,
                    Document = row.Document,
                    ShapeFingerprint = row.ShapeFingerprint,
                    ChangeType = PersistedOperationChangeType.Restore,
                    ChangedAt = now,
                    ChangedReason = null,
                }
            );

            await ctx.SaveChanges(ct).ConfigureAwait(false);
            if (tx is not null)
                await tx.CommitAsync(ct).ConfigureAwait(false);

            await ApplyChangeAsync(tenantKey, id, PersistedOperationChangeType.Restore, null)
                .ConfigureAwait(false);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    // ----- machine documents -----

    /// <summary>
    /// Registers this node's machine documents. Under one lock shared by every node, each document missing from the
    /// store is written with a history row; each whose stored body differs from the generated one is written back
    /// and counted; and a set of documents this node registers for the first time is numbered one past every set of
    /// its machine stored before it. Documents already stored as generated are left alone: no write, no history row,
    /// no broadcast. Another version's documents, older or newer, are deactivated only once no node has registered
    /// them for <paramref name="grace"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every registration writes the <c>updated_at</c> of this node's active documents again once it is a quarter of
    /// <paramref name="grace"/> old, so a document some node still registers always has a recent one. A document of a
    /// slot this node serves that is not its own, and whose <c>updated_at</c> is older than <paramref name="grace"/>
    /// plus that quarter, is collected whatever its version: its nodes were stopped or rolled back. Two hosts on one
    /// database that register two versions of one slot keep both, and so does a node left running on an older
    /// version. The time is counted from no earlier than when this node's version of the slot last became active
    /// (stored, or restored after it was collected), so a node that does not write <c>updated_at</c> again (one built
    /// before it did) still keeps its documents for the grace period after a newer version appears or comes back.
    /// </para>
    /// <para>
    /// A document of this node's that was collected is made active again whenever it registers: the node is
    /// running, so its documents are registered again.
    /// </para>
    /// <para>
    /// An active document of a slot this node does not serve is marked unexposed (its deprecation reason, with the
    /// time in its <c>updated_at</c>), once; a node that serves it clears the mark when it registers. A document
    /// still marked after <paramref name="grace"/> is collected: no node registering since has served its slot, so
    /// its clients reload instead of retrying.
    /// </para>
    /// <para>
    /// Only rows a host generated take part (<see cref="GeneratedDocuments.IsGenerated"/>). A row uploaded under a
    /// reserved prefix before it was reserved is never numbered against, marked or collected, and a version number past
    /// <see cref="int.MaxValue"/> throws rather than wraps.
    /// </para>
    /// </remarks>
    /// <returns>This node's version of each machine, and every stored machine document's version and state.</returns>
    internal async Task<(
        IReadOnlyDictionary<string, int> OwnVersions,
        IReadOnlyDictionary<string, StoredMachineDocument> Stored
    )> RegisterMachineDocumentsAsync(
        IReadOnlyList<GeneratedDocument> documents,
        TimeSpan grace,
        CancellationToken ct
    )
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        // How stale a registered document's updated_at may get before this node writes it again: a quarter of the
        // grace period, so a document some node serves is never mistaken for one nobody does.
        var refresh = grace / 4;
        var changed = new List<(string Id, string ChangeType)>();
        var marked = false;
        var ownVersions = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, StoredMachineDocument> stored;

        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            await using var tx = await LockAsync(
                    ctx,
                    NoTenantSentinel,
                    MachineDocuments.ReservedIdPrefix,
                    ct
                )
                .ConfigureAwait(false);

            // Only the rows a host generated, and this node's own whatever their body, so a body that no longer
            // matches its id is written back. A row uploaded under a reserved prefix before it was reserved is left
            // as it is: never numbered against, marked or collected.
            var own = documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            var rows = (
                await ctx
                    .PersistedOperations.Where(p =>
                        p.TenantKey == NoTenantSentinel
                        && (
                            p.Id.StartsWith(MachineDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(TrainDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ProgressDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ClientDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ConsoleDocuments.ReservedIdPrefix)
                        )
                    )
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            )
                .Where(r => own.Contains(r.Id) || GeneratedDocuments.IsGenerated(r.Id, r.Document))
                .ToList();
            var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var activeSince = await ActiveSinceAsync(ctx, documents, byId, now, ct)
                .ConfigureAwait(false);

            foreach (var machine in documents.GroupBy(d => d.Slot, StringComparer.Ordinal))
            {
                var mine = machine.ToList();
                var ownIds = mine.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                var ofMachine = rows.Where(r =>
                        GeneratedDocuments.SlotOf(r.Id, [machine.Key]) is not null
                    )
                    .ToList();
                // A set already stored keeps the number it registered under; a new one goes after every other.
                var version = mine.All(d => byId.ContainsKey(d.Id))
                    ? mine.Max(d => byId[d.Id].Version)
                    : checked(ofMachine.Select(r => r.Version).DefaultIfEmpty(0).Max() + 1);
                ownVersions[machine.Key] = version;

                foreach (var document in mine)
                    marked |= Register(ctx, document, version, byId, rows, changed, now, refresh);

                // Another version no node has registered for the grace period is not coming back: its nodes were
                // stopped or rolled back, so its clients reload rather than retry. Counted from no earlier than this
                // version last became active, so a node that never writes updated_at again keeps its documents for
                // the grace period after this one appears, or comes back after a rollback.
                var registeredAt = mine.Max(d => activeSince[d.Id]);
                foreach (
                    var stale in ofMachine.Where(r =>
                        r.IsActive
                        && !ownIds.Contains(r.Id)
                        && now - (r.UpdatedAt > registeredAt ? r.UpdatedAt : registeredAt)
                            >= grace + refresh
                    )
                )
                {
                    var reason =
                        $"collected: no node has registered version {stale.Version} of '{machine.Key}' for longer than {grace}";
                    stale.IsActive = false;
                    stale.DeprecationReason = reason;
                    stale.UpdatedAt = now;
                    History(ctx, stale, PersistedOperationChangeType.Deactivate, now, reason);
                    changed.Add((stale.Id, PersistedOperationChangeType.Deactivate));
                }
            }

            var served = ownVersions.Keys.ToList();
            foreach (
                var row in rows.Where(r =>
                    r.IsActive && GeneratedDocuments.SlotOf(r.Id, served) is null
                )
            )
            {
                if (!IsUnexposedMark(row.DeprecationReason))
                {
                    row.DeprecationReason =
                        $"{UnexposedMark} not served by a node that registered its generated documents at {now:O}";
                    row.UpdatedAt = now;
                    marked = true;
                }
                else if (now - row.UpdatedAt >= grace)
                {
                    var reason =
                        $"collected: no node registering its generated documents has served it for longer than {grace}";
                    row.IsActive = false;
                    row.DeprecationReason = reason;
                    row.UpdatedAt = now;
                    History(ctx, row, PersistedOperationChangeType.Deactivate, now, reason);
                    changed.Add((row.Id, PersistedOperationChangeType.Deactivate));
                }
            }

            if (changed.Count > 0 || marked)
                await ctx.SaveChanges(ct).ConfigureAwait(false);
            if (tx is not null)
                await tx.CommitAsync(ct).ConfigureAwait(false);

            stored = rows.ToDictionary(
                r => r.Id,
                r => new StoredMachineDocument(r.Version, r.IsActive),
                StringComparer.Ordinal
            );
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }

        foreach (var (id, changeType) in changed)
        {
            try
            {
                await ApplyChangeAsync(null, id, changeType, null).ConfigureAwait(false);
            }
            catch (PersistedOperationNotBroadcastException)
            {
                // Logged where it was raised. Another node serves only its own machine documents, so it is told of
                // these only to drop what it cached, which its maximum age bounds.
            }
        }

        return (ownVersions, stored);
    }

    // When each of this node's documents last became active: now for one this registration inserts or restores, else
    // its latest insert or restore in the history, else its created_at. A version deployed, rolled back and deployed
    // again restores rows created long ago, and a second node of that version finds them already restored.
    private static async Task<Dictionary<string, DateTime>> ActiveSinceAsync(
        Trax.Effect.Data.Services.DataContext.IDataContext ctx,
        IReadOnlyList<GeneratedDocument> documents,
        Dictionary<string, PersistedOperation> byId,
        DateTime now,
        CancellationToken ct
    )
    {
        var active = documents
            .Select(d => d.Id)
            .Where(id => byId.TryGetValue(id, out var row) && row.IsActive)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var activations = (
            await ctx
                .PersistedOperationHistories.AsNoTracking()
                .Where(h =>
                    h.TenantKey == NoTenantSentinel
                    && active.Contains(h.Id)
                    && (
                        h.ChangeType == PersistedOperationChangeType.Upsert
                        || h.ChangeType == PersistedOperationChangeType.Restore
                    )
                )
                // One row per id, its latest activation, rather than the whole history of each.
                .GroupBy(h => h.Id)
                .Select(g => new { Id = g.Key, At = g.Max(h => h.ChangedAt) })
                .ToListAsync(ct)
                .ConfigureAwait(false)
        ).ToDictionary(a => a.Id, a => a.At, StringComparer.Ordinal);

        var since = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var document in documents)
            since[document.Id] =
                !active.Contains(document.Id) ? now
                : activations.TryGetValue(document.Id, out var at)
                && at > byId[document.Id].CreatedAt
                    ? at
                : byId[document.Id].CreatedAt;
        return since;
    }

    // The deprecation reason an active machine document carries while no node registering serves its machine.
    private const string UnexposedMark = "unexposed:";

    private static bool IsUnexposedMark(string? reason) =>
        reason is not null && reason.StartsWith(UnexposedMark, StringComparison.Ordinal);

    // Writes one generated document: a new row, a row written back over a body that does not match its id, a row
    // collected earlier that this node serves again, a row whose unexposed mark this node clears, or an active row
    // whose updated_at is older than refresh, which records that a node still registers it.
    // Returns whether it changed a row without a history entry (the mark or the refresh).
    private bool Register(
        Trax.Effect.Data.Services.DataContext.IDataContext ctx,
        GeneratedDocument document,
        int version,
        Dictionary<string, PersistedOperation> byId,
        List<PersistedOperation> rows,
        List<(string Id, string ChangeType)> changed,
        DateTime now,
        TimeSpan refresh
    )
    {
        if (!byId.TryGetValue(document.Id, out var row))
        {
            row = new PersistedOperation
            {
                TenantKey = NoTenantSentinel,
                Id = document.Id,
                OperationName = ExtractOperationName(document.Text),
                Version = version,
                Document = document.Text,
                ShapeFingerprint = ShapeFingerprintComputer.Compute(document.Text),
                IsActive = true,
                Description = document.Description,
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.PersistedOperations.Add(row);
            rows.Add(row);
            byId[row.Id] = row;
            History(ctx, row, PersistedOperationChangeType.Upsert, now, "registered");
            changed.Add((row.Id, PersistedOperationChangeType.Upsert));
        }
        else if (!string.Equals(row.Document, document.Text, StringComparison.Ordinal))
        {
            row.Document = document.Text;
            row.ShapeFingerprint = ShapeFingerprintComputer.Compute(document.Text);
            row.OperationName = ExtractOperationName(document.Text);
            row.IsActive = true;
            row.DeprecationReason = null;
            row.UpdatedAt = now;
            History(
                ctx,
                row,
                PersistedOperationChangeType.Upsert,
                now,
                "repaired: the stored body did not match its id"
            );
            changed.Add((row.Id, PersistedOperationChangeType.Upsert));
            MachineDocumentInstruments.Repairs.Add(
                1,
                new KeyValuePair<string, object?>("slot", document.Slot)
            );
            _logger.LogError(
                "The stored body of generated document '{Id}' did not match its id, so it was written back. Find who "
                    + "changed the row.",
                row.Id
            );
        }
        else if (!row.IsActive)
        {
            row.IsActive = true;
            row.DeprecationReason = null;
            row.UpdatedAt = now;
            History(ctx, row, PersistedOperationChangeType.Restore, now, null);
            changed.Add((row.Id, PersistedOperationChangeType.Restore));
        }
        else if (IsUnexposedMark(row.DeprecationReason))
        {
            row.DeprecationReason = null;
            row.UpdatedAt = now;
            return true;
        }
        else if (now - row.UpdatedAt >= refresh)
        {
            row.UpdatedAt = now;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The stored body of the active generated document <paramref name="id"/>, or null when the store holds none or its
    /// body is not the one its id names.
    /// </summary>
    internal async Task<string?> ReadGeneratedDocumentAsync(string id, CancellationToken ct)
    {
        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            var document = await ctx
                .PersistedOperations.AsNoTracking()
                .Where(p => p.TenantKey == NoTenantSentinel && p.Id == id && p.IsActive)
                .Select(p => p.Document)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            return document is not null && GeneratedDocuments.IsGenerated(id, document)
                ? document
                : null;
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The version and state of every stored generated document, active or not, by id. A row under a reserved prefix
    /// that no host generated is left out.
    /// </summary>
    internal async Task<
        IReadOnlyDictionary<string, StoredMachineDocument>
    > ReadMachineDocumentVersionsAsync(CancellationToken ct)
    {
        var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        try
        {
            return (
                await ctx
                    .PersistedOperations.AsNoTracking()
                    .Where(p =>
                        p.TenantKey == NoTenantSentinel
                        && (
                            p.Id.StartsWith(MachineDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(TrainDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ProgressDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ClientDocuments.ReservedIdPrefix)
                            || p.Id.StartsWith(ConsoleDocuments.ReservedIdPrefix)
                        )
                    )
                    .Select(p => new
                    {
                        p.Id,
                        p.Version,
                        p.IsActive,
                        p.Document,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false)
            ).Where(p => GeneratedDocuments.IsGenerated(p.Id, p.Document)).ToDictionary(p => p.Id, p => new StoredMachineDocument(p.Version, p.IsActive), StringComparer.Ordinal);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    private static void History(
        Trax.Effect.Data.Services.DataContext.IDataContext ctx,
        PersistedOperation row,
        string changeType,
        DateTime at,
        string? reason
    ) =>
        ctx.PersistedOperationHistories.Add(
            new PersistedOperationHistory
            {
                TenantKey = row.TenantKey,
                Id = row.Id,
                Document = row.Document,
                ShapeFingerprint = row.ShapeFingerprint,
                ChangeType = changeType,
                ChangedAt = at,
                ChangedReason = reason,
            }
        );

    /// <summary>
    /// Refuses a write to an id under a reserved prefix: those are the documents the host registers itself, when it
    /// starts.
    /// </summary>
    private static void RefuseReserved(string id)
    {
        if (GeneratedDocuments.PrefixOf(id) is { } prefix)
            throw new PersistedOperationInputException(
                $"The id '{id}' is under '{prefix}', which is reserved for the documents the host "
                    + "registers itself. Choose another id."
            );
    }

    // ----- helpers -----

    /// <summary>
    /// Opens a transaction that holds the only write lock on <c>(tenant, id)</c> until it ends, so
    /// changes to one id are applied one at a time and each reads the row the previous one left.
    /// </summary>
    /// <remarks>
    /// On PostgreSQL a transaction-scoped advisory lock keyed on the id, which also covers an id
    /// that has no row yet (a row lock cannot). SQLite's transactions take the database write lock
    /// when they begin, which serializes them already. The in-memory provider has no
    /// transactions and returns null; it runs in one process for tests.
    /// </remarks>
    private static async Task<IDbContextTransaction?> LockAsync(
        Trax.Effect.Data.Services.DataContext.IDataContext ctx,
        string tenantSentinel,
        string id,
        CancellationToken ct
    )
    {
        var database = ((DbContext)ctx).Database;
        if (!database.IsRelational())
            return null;

        var tx = await database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            var key = $"trax.persisted_operation\u001f{tenantSentinel}\u001f{id}";
            await database
                .ExecuteSqlInterpolatedAsync(
                    $"select pg_advisory_xact_lock(hashtextextended({key}, 0))",
                    ct
                )
                .ConfigureAwait(false);
        }

        return tx;
    }

    /// <summary>
    /// Applies a committed change to this node's caches and broadcasts it to the others. Runs
    /// without the caller's cancellation token: once the change is saved, it must reach the
    /// caches whether or not the caller is still waiting.
    /// </summary>
    /// <exception cref="PersistedOperationNotBroadcastException">
    /// The broadcast was not confirmed. The change is saved and applied here.
    /// </exception>
    private async Task ApplyChangeAsync(
        string? tenantKey,
        string id,
        string changeType,
        PersistedOperation? saved
    )
    {
        _cache.Invalidate(tenantKey, id);
        await _hcInvalidator.InvalidateAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await _broadcaster
                .PublishAsync(
                    new PersistedOperationChangedMessage(
                        tenantKey,
                        id,
                        changeType,
                        _clock.GetUtcNow().UtcDateTime
                    ),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Persisted operation '{Id}' was changed ({ChangeType}) but the change could not be broadcast; "
                    + "other nodes serve what they cached until it reaches its maximum age.",
                id,
                changeType
            );
            throw new PersistedOperationNotBroadcastException(id, saved, ex);
        }
    }

    /// <summary>
    /// A persisted document holds exactly one operation, so its id names one thing to run and the
    /// shape fingerprint has one operation to describe. Checked here rather than in the validator
    /// so a host using the no-op validator gets the same refusal.
    /// </summary>
    private static void RequireExactlyOneOperation(string document)
    {
        DocumentNode parsed;
        try
        {
            parsed = Utf8GraphQLParser.Parse(document);
        }
        catch (SyntaxException ex)
        {
            throw new PersistedOperationParseException(ex.Message, ex.Line, ex.Column, ex);
        }

        var operations = parsed.Definitions.OfType<OperationDefinitionNode>().Count();
        if (operations != 1)
            throw new PersistedOperationInputException(
                $"A persisted operation document must contain exactly one operation; this one contains {operations}."
            );
    }

    private static string Normalize(string? tenantKey) =>
        string.IsNullOrEmpty(tenantKey) ? NoTenantSentinel : tenantKey;

    /// <summary>
    /// Returns the GraphQL operation definition's name from the document, or
    /// the empty string when the operation is anonymous. Convention: each
    /// persisted document holds exactly one operation, so picking the first
    /// definition is unambiguous.
    /// </summary>
    private static string ExtractOperationName(string document)
    {
        try
        {
            var parsed = Utf8GraphQLParser.Parse(document);
            var op = parsed.Definitions.OfType<OperationDefinitionNode>().FirstOrDefault();
            return op?.Name?.Value ?? string.Empty;
        }
        catch
        {
            // Validator already ran; if parse fails here it's surprising.
            // Fall back to empty rather than crash the upsert.
            return string.Empty;
        }
    }

    private static PersistedOperation Denormalize(PersistedOperation row) =>
        new()
        {
            TenantKey = string.IsNullOrEmpty(row.TenantKey) ? null : row.TenantKey,
            Id = row.Id,
            OperationName = row.OperationName,
            Version = row.Version,
            Document = row.Document,
            ShapeFingerprint = row.ShapeFingerprint,
            IsActive = row.IsActive,
            DeprecationReason = row.DeprecationReason,
            Description = row.Description,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
        };

    private static ValueTask DisposeContextAsync(
        Trax.Effect.Data.Services.DataContext.IDataContext ctx
    ) => ctx is IAsyncDisposable async ? async.DisposeAsync() : new ValueTask(Task.CompletedTask);
}
