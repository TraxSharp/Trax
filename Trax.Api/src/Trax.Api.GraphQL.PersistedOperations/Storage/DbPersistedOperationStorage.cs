using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.ShapeDiff;
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
    private readonly TimeProvider _clock;
    private readonly ILogger<DbPersistedOperationStorage> _logger;

    public DbPersistedOperationStorage(
        IDataContextProviderFactory factory,
        PersistedOperationsOptions options,
        IPersistedOperationCache cache,
        IPersistedOperationBroadcaster broadcaster,
        IPersistedOperationValidator validator,
        HotChocolateOperationCacheInvalidator hcInvalidator,
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
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _options = options;
        _cache = cache;
        _broadcaster = broadcaster;
        _validator = validator;
        _hcInvalidator = hcInvalidator;
        _clock = clock;
        _logger = logger;
    }

    // ----- IOperationDocumentStorage (HC hot path) -----

    public async ValueTask<IOperationDocument?> TryReadAsync(
        OperationDocumentId documentId,
        CancellationToken cancellationToken
    )
    {
        if (documentId.IsEmpty)
            return null;

        var id = documentId.Value;
        // v1 has no tenant resolver; hot-path lookups always use the null-tenant row set.
        var tenantKey = (string?)null;

        var cached = _cache.TryGet(tenantKey, id);
        if (cached is not null)
            return new OperationDocumentSourceText(cached);

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
                return null;

            _cache.Set(tenantKey, id, document);
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

            _cache.Invalidate(tenantKey, id);
            await _hcInvalidator.InvalidateAsync(ct).ConfigureAwait(false);
            await PublishAsync(tenantKey, id, PersistedOperationChangeType.Upsert, ct)
                .ConfigureAwait(false);

            return Denormalize(existing);
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

            _cache.Invalidate(tenantKey, id);
            await _hcInvalidator.InvalidateAsync(ct).ConfigureAwait(false);
            await PublishAsync(tenantKey, id, PersistedOperationChangeType.Deactivate, ct)
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

            _cache.Invalidate(tenantKey, id);
            await _hcInvalidator.InvalidateAsync(ct).ConfigureAwait(false);
            await PublishAsync(tenantKey, id, PersistedOperationChangeType.Restore, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            await DisposeContextAsync(ctx).ConfigureAwait(false);
        }
    }

    // ----- helpers -----

    private async Task PublishAsync(
        string? tenantKey,
        string id,
        string changeType,
        CancellationToken ct
    )
    {
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
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Broadcaster errors must never fail the user-visible operation.
            _logger.LogWarning(
                ex,
                "Persisted-operation broadcaster failed to publish change for id '{Id}'.",
                id
            );
        }
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
