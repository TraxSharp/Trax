using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Body of the <c>operations.persistedOperations</c> query namespace. Reads
/// hit the Trax data context directly via <see cref="IDataContextProviderFactory"/>.
/// </summary>
public sealed class PersistedOperationQueries
{
    /// <summary>List persisted operations, newest-first, paginated.</summary>
    public async Task<PersistedOperationsPage> PersistedOperations(
        [Service] IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        PersistedOperationFilter? filter = null,
        int skip = 0,
        int take = 50
    )
    {
        if (take is <= 0 or > 200)
            take = 50;
        if (skip < 0)
            skip = 0;

        await using var ctx = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
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

    /// <summary>Look up a single persisted operation. Returns null when missing.</summary>
    public async Task<PersistedOperationDto?> PersistedOperation(
        string id,
        [Service] IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        string? tenantKey = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var sentinel = tenantKey ?? string.Empty;
        await using var ctx = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx
            .PersistedOperations.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantKey == sentinel && p.Id == id, ct)
            .ConfigureAwait(false);
        return row is null ? null : PersistedOperationDto.From(row);
    }

    /// <summary>Audit history for an operation, most-recent-first.</summary>
    public async Task<IReadOnlyList<PersistedOperationHistoryDto>> PersistedOperationHistory(
        string id,
        [Service] IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        string? tenantKey = null,
        int skip = 0,
        int take = 50
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (take is <= 0 or > 200)
            take = 50;
        if (skip < 0)
            skip = 0;
        var sentinel = tenantKey ?? string.Empty;

        await using var ctx = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
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
}
