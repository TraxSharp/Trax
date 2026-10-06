using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
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
        ILogger<DbPersistedOperationStorage> logger
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

        if (_misses.TryGet(id, out _))
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
                _misses.Add(id, id, generation, readAt);
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
