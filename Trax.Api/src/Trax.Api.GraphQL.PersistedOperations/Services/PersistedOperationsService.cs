using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The one implementation of <see cref="IPersistedOperationsService"/>: writes go through
/// <see cref="IPersistedOperationStore"/>, which validates, records history and invalidates the
/// caches; reads go to the Trax data context.
/// </summary>
internal sealed class PersistedOperationsService : IPersistedOperationsService
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    private IPersistedOperationStore? _store;
    private IDataContextProviderFactory? _contextFactory;

    public PersistedOperationsService(
        IPersistedOperationStore store,
        IDataContextProviderFactory contextFactory
    )
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(contextFactory);
        _store = store;
        _contextFactory = contextFactory;
    }

    private PersistedOperationsService() { }

    // For the resolver overloads kept for callers that pass only the dependency the one method
    // they call needs. Each of those methods touches only what it was given.
    internal static PersistedOperationsService ForStore(IPersistedOperationStore store) =>
        new() { _store = store ?? throw new ArgumentNullException(nameof(store)) };

    internal static PersistedOperationsService ForReads(
        IDataContextProviderFactory contextFactory
    ) =>
        new()
        {
            _contextFactory =
                contextFactory ?? throw new ArgumentNullException(nameof(contextFactory)),
        };

    private IPersistedOperationStore Store =>
        _store ?? throw new InvalidOperationException("This instance was built without a store.");

    private IDataContextProviderFactory ContextFactory =>
        _contextFactory
        ?? throw new InvalidOperationException("This instance was built without a data context.");

    public async Task<PersistedOperationsPage> ListAsync(
        PersistedOperationFilter? filter,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        (skip, take) = ClampPage(skip, take);

        await using var ctx = await ContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<Trax.Effect.Models.PersistedOperation.PersistedOperation> q =
            ctx.PersistedOperations.AsNoTracking();

        if (filter is not null)
        {
            if (filter.IsActive.HasValue)
                q = q.Where(p => p.IsActive == filter.IsActive.Value);
            if (!string.IsNullOrWhiteSpace(filter.TenantKey))
                q = q.Where(p => p.TenantKey == filter.TenantKey);
            else if (filter.TenantKey == "")
                q = q.Where(p => p.TenantKey == "");
            if (!string.IsNullOrWhiteSpace(filter.IdStartsWith))
                q = q.Where(p => p.Id.StartsWith(filter.IdStartsWith));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderByDescending(p => p.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .Select(p => new PersistedOperationDto(
                p.Id,
                p.TenantKey == "" ? null : p.TenantKey,
                p.OperationName,
                p.Version,
                p.Document,
                p.ShapeFingerprint,
                p.IsActive,
                p.DeprecationReason,
                p.Description,
                p.CreatedAt,
                p.UpdatedAt
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PersistedOperationsPage(items, total);
    }

    public async Task<PersistedOperationDto?> GetAsync(
        string id,
        string? tenantKey,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var row = await FindAnyAsync(id, tenantKey, ct).ConfigureAwait(false);
        return row is null ? null : PersistedOperationDto.From(row);
    }

    public async Task<IReadOnlyList<PersistedOperationHistoryDto>> GetHistoryAsync(
        string id,
        string? tenantKey,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        (skip, take) = ClampPage(skip, take);
        var sentinel = tenantKey ?? string.Empty;

        await using var ctx = await ContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx
            .PersistedOperationHistories.AsNoTracking()
            .Where(h => h.TenantKey == sentinel && h.Id == id)
            .OrderByDescending(h => h.HistoryId)
            .Skip(skip)
            .Take(take)
            .Select(h => new PersistedOperationHistoryDto(
                h.HistoryId,
                h.Id,
                h.TenantKey == "" ? null : h.TenantKey,
                h.Document,
                h.ShapeFingerprint,
                h.ChangeType,
                h.ChangedAt,
                h.ChangedReason
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<UploadPersistedOperationPayload> UploadAsync(
        UploadPersistedOperationInput input,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return UploadPersistedOperationPayload.Fail(InvalidInput("id is required."));
        if (string.IsNullOrWhiteSpace(input.Document))
            return UploadPersistedOperationPayload.Fail(InvalidInput("document is required."));

        try
        {
            var row = await Store
                .UpsertAsync(
                    input.Id,
                    input.Document,
                    new UpsertOptions
                    {
                        TenantKey = input.TenantKey,
                        Description = input.Description,
                        BypassShapeDiff = input.BypassShapeDiff,
                        Version = input.Version,
                    },
                    ct
                )
                .ConfigureAwait(false);
            return UploadPersistedOperationPayload.Ok(PersistedOperationDto.From(row));
        }
        catch (PersistedOperationNotBroadcastException ex) when (ex.Operation is not null)
        {
            // Saved, so the payload carries the operation; not every node was told, so it is
            // not a success.
            return new UploadPersistedOperationPayload(
                PersistedOperationDto.From(ex.Operation),
                [PersistedOperationError.FromNotBroadcast(ex)]
            );
        }
        catch (PersistedOperationParseException ex)
        {
            return UploadPersistedOperationPayload.Fail(
                PersistedOperationError.FromParseException(ex)
            );
        }
        catch (PersistedOperationValidationException ex)
        {
            return UploadPersistedOperationPayload.Fail(
                PersistedOperationError.FromValidationException(ex)
            );
        }
        catch (ShapeDiffViolationException ex)
        {
            return UploadPersistedOperationPayload.Fail(PersistedOperationError.FromShapeDiff(ex));
        }
        catch (PersistedOperationInputException ex)
        {
            return UploadPersistedOperationPayload.Fail(
                new PersistedOperationError(ex.Code, ex.Message, null, null, null, null)
            );
        }
    }

    public async Task<DeactivatePersistedOperationPayload> DeactivateAsync(
        DeactivatePersistedOperationInput input,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return new DeactivatePersistedOperationPayload(null, [InvalidInput("id is required.")]);
        if (string.IsNullOrWhiteSpace(input.Reason))
            return new DeactivatePersistedOperationPayload(
                null,
                [InvalidInput("reason is required.")]
            );

        // Any row, active or not: deactivating an inactive operation is not an error, and doing it
        // again sends the change to every node again.
        // A caller that supplied only a store (the overloads kept for compiled callers) sees
        // active operations only.
        var existing = _contextFactory is null
            ? await Store.GetAsync(input.Id, input.TenantKey, ct).ConfigureAwait(false)
            : await FindAnyAsync(input.Id, input.TenantKey, ct).ConfigureAwait(false);
        if (existing is null)
            return new DeactivatePersistedOperationPayload(
                null,
                [PersistedOperationError.NotFound(input.Id)]
            );

        var errors = await NotBroadcastErrorsAsync(
                Store.DeactivateAsync(input.Id, input.TenantKey, input.Reason, ct)
            )
            .ConfigureAwait(false);
        existing.IsActive = false;
        existing.DeprecationReason = input.Reason;
        return new DeactivatePersistedOperationPayload(
            PersistedOperationDto.From(existing),
            errors
        );
    }

    public async Task<RestorePersistedOperationPayload> RestoreAsync(
        RestorePersistedOperationInput input,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return new RestorePersistedOperationPayload(null, [InvalidInput("id is required.")]);

        // Restore applies to a deactivated row, which the store's GetAsync does not return.
        var raw = await FindAnyAsync(input.Id, input.TenantKey, ct).ConfigureAwait(false);
        if (raw is null)
            return new RestorePersistedOperationPayload(
                null,
                [PersistedOperationError.NotFound(input.Id)]
            );

        var errors = await NotBroadcastErrorsAsync(
                Store.RestoreAsync(input.Id, input.TenantKey, ct)
            )
            .ConfigureAwait(false);
        raw.IsActive = true;
        raw.DeprecationReason = null;
        return new RestorePersistedOperationPayload(PersistedOperationDto.From(raw), errors);
    }

    /// <summary>
    /// Runs a deactivation or restore. A change that was saved but not broadcast is still the
    /// operation's new state, so it comes back as a payload error beside it rather than a throw.
    /// </summary>
    private static async Task<IReadOnlyList<PersistedOperationError>> NotBroadcastErrorsAsync(
        Task change
    )
    {
        try
        {
            await change.ConfigureAwait(false);
            return Array.Empty<PersistedOperationError>();
        }
        catch (PersistedOperationNotBroadcastException ex)
        {
            return [PersistedOperationError.FromNotBroadcast(ex)];
        }
    }

    private async Task<Trax.Effect.Models.PersistedOperation.PersistedOperation?> FindAnyAsync(
        string id,
        string? tenantKey,
        CancellationToken ct
    )
    {
        var sentinel = tenantKey ?? string.Empty;
        await using var ctx = await ContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx
            .PersistedOperations.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantKey == sentinel && p.Id == id, ct)
            .ConfigureAwait(false);
    }

    private static (int Skip, int Take) ClampPage(int skip, int take) =>
        (skip < 0 ? 0 : skip, take is <= 0 or > MaxTake ? DefaultTake : take);

    private static PersistedOperationError InvalidInput(string message) =>
        new(PersistedOperationInputException.CodeValue, message, null, null, null, null);
}
