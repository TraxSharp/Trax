using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Predefined operational queries: health, trains, manifests, manifest groups, and execution history.
/// </summary>
public class OperationsQueries
{
    public async Task<HealthStatus> GetHealth(
        [Service] ITraxHealthService healthService,
        CancellationToken ct
    )
    {
        return await healthService.GetHealthAsync(ct);
    }

    public IReadOnlyList<TrainInfo> GetTrains([Service] ITrainDiscoveryService discoveryService)
    {
        return discoveryService
            .DiscoverTrains()
            .Select(r => new TrainInfo(
                r.ServiceTypeName,
                r.ImplementationTypeName,
                r.InputTypeName,
                r.OutputTypeName,
                r.Lifetime.ToString(),
                GetInputSchema(r.InputType),
                r.RequiredPolicies,
                r.RequiredRoles,
                r.IsQuery,
                r.IsMutation,
                r.GraphQLName,
                r.IsBroadcastEnabled
            ))
            .ToList();
    }

    public async Task<PagedResult<ManifestSummary>> GetManifests(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.Manifests.AsNoTracking().OrderByDescending(m => m.Id);

        // Count: use estimate for unfiltered queries, exact for cursor-filtered
        var (totalCount, isEstimate) = afterId.HasValue
            ? (await baseQuery.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                "manifest",
                () => baseQuery.CountAsync(ct),
                ct
            );

        // Keyset cursor: skip to items after the cursor instead of using OFFSET
        var query = afterId.HasValue ? baseQuery.Where(m => m.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(m => new ManifestSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.IsEnabled,
                m.ScheduleType,
                m.CronExpression,
                m.IntervalSeconds,
                m.MaxRetries,
                m.TimeoutSeconds,
                m.LastSuccessfulRun,
                m.ManifestGroupId,
                m.DependsOnManifestId,
                m.Priority
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<ManifestSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }

    public async Task<ManifestSummary?> GetManifest(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .Manifests.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ManifestSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.IsEnabled,
                m.ScheduleType,
                m.CronExpression,
                m.IntervalSeconds,
                m.MaxRetries,
                m.TimeoutSeconds,
                m.LastSuccessfulRun,
                m.ManifestGroupId,
                m.DependsOnManifestId,
                m.Priority
            ))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<ManifestGroupSummary>> GetManifestGroups(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.ManifestGroups.AsNoTracking().OrderByDescending(g => g.Id);

        var (totalCount, isEstimate) = afterId.HasValue
            ? (await baseQuery.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                "manifest_group",
                () => baseQuery.CountAsync(ct),
                ct
            );

        var query = afterId.HasValue ? baseQuery.Where(g => g.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(g => new ManifestGroupSummary(
                g.Id,
                g.Name,
                g.MaxActiveJobs,
                g.Priority,
                g.IsEnabled,
                g.CreatedAt,
                g.UpdatedAt
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<ManifestGroupSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }

    public async Task<PagedResult<ExecutionSummary>> GetExecutions(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        // Executions are ordered by StartTime DESC, but keyset cursor uses Id
        // since it's monotonically increasing and indexed.
        var baseQuery = db.Metadatas.AsNoTracking().OrderByDescending(m => m.Id);

        var (totalCount, isEstimate) = afterId.HasValue
            ? (await baseQuery.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                "metadata",
                () => baseQuery.CountAsync(ct),
                ct
            );

        var query = afterId.HasValue ? baseQuery.Where(m => m.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(m => new ExecutionSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureJunction,
                m.FailureReason,
                m.ManifestId,
                m.CancellationRequested,
                m.HostName,
                m.HostEnvironment,
                m.HostInstanceId
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<ExecutionSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }

    public async Task<ExecutionSummary?> GetExecution(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ExecutionSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureJunction,
                m.FailureReason,
                m.ManifestId,
                m.CancellationRequested,
                m.HostName,
                m.HostEnvironment,
                m.HostInstanceId
            ))
            .FirstOrDefaultAsync(ct);
    }

    private static List<InputPropertySchema> GetInputSchema(Type inputType)
    {
        return inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => new InputPropertySchema(
                p.Name,
                GetFriendlyTypeName(p.PropertyType),
                Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || !p.PropertyType.IsValueType
            ))
            .ToList();
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{GetFriendlyTypeName(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{name}<{args}>";
    }
}
