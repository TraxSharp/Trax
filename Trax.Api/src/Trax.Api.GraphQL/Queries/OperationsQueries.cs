using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Predefined operational queries: health, trains, manifests, manifest groups, execution
/// history, and the nested <c>deadLetters</c> namespace.
/// </summary>
public class OperationsQueries
{
    /// <summary>
    /// Nested namespace exposing dead letter queries (<c>deadLetters</c>, <c>deadLetter</c>).
    /// </summary>
    public DeadLetterQueries DeadLetters() => new();

    /// <summary>
    /// Nested namespace exposing work queue queries (<c>workQueues</c>, <c>workQueue</c>).
    /// </summary>
    public WorkQueueQueries WorkQueue() => new();

    /// <summary>
    /// Nested namespace exposing manifest group queries (<c>graph</c>).
    /// </summary>
    public ManifestGroupQueries ManifestGroups() => new();

    /// <summary>
    /// Nested namespace exposing log queries (paginated reads of <c>trax.log</c>).
    /// </summary>
    public LogQueries Logs() => new();

    /// <summary>
    /// Nested namespace exposing dashboard / server metrics. Same data the dashboard
    /// Index page renders.
    /// </summary>
    public MetricsQueries Metrics() => new();

    /// <summary>
    /// Nested namespace exposing live scheduler runtime config (what the dashboard's
    /// ServerSettingsPage reads).
    /// </summary>
    public ConfigQueries Config() => new();

    public async Task<HealthStatus> GetHealth(
        [Service] ITraxHealthService healthService,
        CancellationToken ct
    )
    {
        return await healthService.GetHealthAsync(ct);
    }

    public IReadOnlyList<TrainInfo> GetTrains(
        [Service] ITrainDiscoveryService discoveryService,
        bool hideAdminTrains = false
    )
    {
        IEnumerable<TrainRegistration> registrations = discoveryService.DiscoverTrains();

        // AdminTrains.FullNames is the canonical list (interface FullName, per CLAUDE.md
        // naming rules). Compare against ServiceType.FullName for an exact match.
        if (hideAdminTrains)
        {
            var adminNames = AdminTrains.FullNames.ToHashSet();
            registrations = registrations.Where(r => !adminNames.Contains(r.ServiceType.FullName!));
        }

        return registrations
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
        bool? isEnabled = null,
        ScheduleType? scheduleType = null,
        string? nameContains = null,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.Manifest.Manifest> baseQuery = db
            .Manifests.AsNoTracking()
            .OrderByDescending(m => m.Id);

        if (isEnabled.HasValue)
            baseQuery = baseQuery.Where(m => m.IsEnabled == isEnabled.Value);
        if (scheduleType.HasValue)
            baseQuery = baseQuery.Where(m => m.ScheduleType == scheduleType.Value);
        if (!string.IsNullOrWhiteSpace(nameContains))
            baseQuery = baseQuery.Where(m => m.Name.Contains(nameContains));

        var hasFilter =
            isEnabled.HasValue || scheduleType.HasValue || !string.IsNullOrWhiteSpace(nameContains);

        // Count: estimate only for the unfiltered first page, exact when filtered or cursored.
        var (totalCount, isEstimate) =
            (afterId.HasValue || hasFilter)
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

    public async Task<PagedResult<ExecutionSummary>> GetExecutions(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        TrainState? trainState = null,
        string? trainName = null,
        DateTime? startedAfter = null,
        DateTime? startedBefore = null,
        SortOrder order = SortOrder.Newest,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.Metadata.Metadata> filtered = db.Metadatas.AsNoTracking();

        if (trainState.HasValue)
            filtered = filtered.Where(m => m.TrainState == trainState.Value);
        if (!string.IsNullOrWhiteSpace(trainName))
            filtered = filtered.Where(m => m.Name == trainName);
        if (startedAfter.HasValue)
            filtered = filtered.Where(m => m.StartTime >= startedAfter.Value);
        if (startedBefore.HasValue)
            filtered = filtered.Where(m => m.StartTime <= startedBefore.Value);

        var hasFilter =
            trainState.HasValue
            || !string.IsNullOrWhiteSpace(trainName)
            || startedAfter.HasValue
            || startedBefore.HasValue;

        // Filters (or a cursor) force an exact count; the estimator only applies to the
        // unfiltered first page.
        var (totalCount, isEstimate) =
            (afterId.HasValue || hasFilter)
                ? (await filtered.CountAsync(ct), false)
                : await CountEstimator.EstimateOrCountAsync(
                    db,
                    "metadata",
                    () => filtered.CountAsync(ct),
                    ct
                );

        // Keyset stays safe in both directions: Newest pages id < afterId (DESC), Oldest
        // pages id > afterId (ASC). Both use the primary key index.
        var oldest = order == SortOrder.Oldest;
        var query = filtered;
        if (afterId.HasValue)
            query = oldest
                ? query.Where(m => m.Id > afterId.Value)
                : query.Where(m => m.Id < afterId.Value);
        query = oldest ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id);

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

    public async Task<ExecutionDetail?> GetExecutionDetail(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var detail = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ExecutionDetail(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureJunction,
                m.FailureReason,
                m.FailureException,
                m.StackTrace,
                m.Input,
                m.Output,
                m.ManifestId,
                m.CancellationRequested,
                m.CurrentlyRunningJunction,
                m.JunctionStartedAt,
                m.HostName,
                m.HostEnvironment,
                m.HostInstanceId
            ))
            .FirstOrDefaultAsync(ct);

        if (detail is null)
            return null;

        // parent_id is covered by the partial index ix_metadata_parent_id, so counting
        // children stays cheap even on the huge metadata table.
        var childCount = await db.Metadatas.AsNoTracking().CountAsync(c => c.ParentId == id, ct);
        return detail with { ChildCount = childCount };
    }

    /// <summary>
    /// Paginated child executions of a parent (metadata rows whose <c>parent_id</c> matches).
    /// Keyset-paginated on id like the top-level executions list.
    /// </summary>
    public async Task<PagedResult<ExecutionSummary>> GetExecutionChildren(
        long parentId,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int take = 25,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.Metadatas.AsNoTracking().Where(m => m.ParentId == parentId);
        var totalCount = await baseQuery.CountAsync(ct);

        var query = afterId.HasValue ? baseQuery.Where(m => m.Id < afterId.Value) : baseQuery;

        var items = await query
            .OrderByDescending(m => m.Id)
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
        return new PagedResult<ExecutionSummary>(items, totalCount, 0, take, false, nextCursor);
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
