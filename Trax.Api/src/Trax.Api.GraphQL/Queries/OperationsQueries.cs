using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectRegistry;
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

    /// <summary>
    /// Canonical FullNames of the internal/administrative scheduler trains (JobDispatcher,
    /// ManifestManager, JobRunner, cleanup, etc.). Clients filter these out of live subscription
    /// feeds; the <c>executions</c> query filters them server-side via <c>hideAdminTrains</c>.
    /// </summary>
    public IReadOnlyList<string> GetAdminTrainNames() => AdminTrains.FullNames;

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

    /// <summary>
    /// The observational effects registered in THIS process, with their enabled + toggleable state.
    /// Read-only: the registry is an in-memory per-process singleton, so this reflects the API host
    /// only, not the scheduler/worker processes where effects run. Backs the dashboard effects list.
    /// </summary>
    public IReadOnlyList<EffectInfo> GetEffects([Service] IEffectRegistry registry)
    {
        return registry
            .GetAll()
            .Select(kvp => new EffectInfo(
                kvp.Key.Name,
                kvp.Key.FullName ?? kvp.Key.Name,
                kvp.Value,
                registry.IsToggleable(kvp.Key)
            ))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The schedule exclusion windows configured on a manifest (the days/dates/ranges/time windows
    /// during which it is intentionally skipped). Empty when the manifest has none or does not
    /// exist. Backs the exclusions panel on the dashboard's manifest detail page.
    /// </summary>
    public async Task<IReadOnlyList<ManifestExclusion>> GetManifestExclusions(
        long manifestId,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);
        var manifest = await db
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == manifestId, ct);
        if (manifest is null)
            return Array.Empty<ManifestExclusion>();

        return manifest
            .GetExclusions()
            .Select(e => new ManifestExclusion(
                e.Type,
                e.DaysOfWeek,
                e.Dates,
                e.StartDate,
                e.EndDate,
                e.StartTime,
                e.EndTime
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
        long? afterId = null,
        long? manifestGroupId = null
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
        if (manifestGroupId.HasValue)
            baseQuery = baseQuery.Where(m => m.ManifestGroupId == manifestGroupId.Value);

        var hasFilter =
            isEnabled.HasValue
            || scheduleType.HasValue
            || !string.IsNullOrWhiteSpace(nameContains)
            || manifestGroupId.HasValue;

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

    /// <summary>
    /// Execution roll-up for a single manifest: run counts by state plus the most recent run and
    /// most recent successful run. Backs the summary cards on the dashboard's manifest detail page.
    /// Served index-only by ix_metadata_manifest_state.
    /// </summary>
    public async Task<ManifestExecutionStats> GetManifestStats(
        long manifestId,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);
        var scoped = db.Metadatas.AsNoTracking().Where(m => m.ManifestId == manifestId);

        var byState = await scoped
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        long CountOf(TrainState state) => byState.FirstOrDefault(x => x.State == state)?.Count ?? 0;

        var lastRun = await scoped.MaxAsync(m => (DateTime?)m.StartTime, ct);
        var lastSuccessfulRun = await scoped
            .Where(m => m.TrainState == TrainState.Completed && m.EndTime != null)
            .MaxAsync(m => (DateTime?)m.EndTime, ct);

        return new ManifestExecutionStats(
            manifestId,
            Total: byState.Sum(x => x.Count),
            Completed: CountOf(TrainState.Completed),
            Failed: CountOf(TrainState.Failed),
            InProgress: CountOf(TrainState.InProgress),
            Pending: CountOf(TrainState.Pending),
            Cancelled: CountOf(TrainState.Cancelled),
            LastRun: lastRun,
            LastSuccessfulRun: lastSuccessfulRun
        );
    }

    /// <summary>
    /// Execution roll-up for one train, keyed by its interface FullName (the value stored in
    /// <c>metadata.Name</c>). Backs the per-train detail page. The state grouping and
    /// <c>ix_metadata_*</c> indexes keep this cheap even against a large metadata table.
    /// </summary>
    public async Task<TrainExecutionStats> GetTrainStats(
        string trainName,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);
        var scoped = db.Metadatas.AsNoTracking().Where(m => m.Name == trainName);

        var byState = await scoped
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        long CountOf(TrainState state) => byState.FirstOrDefault(x => x.State == state)?.Count ?? 0;

        var lastRun = await scoped.MaxAsync(m => (DateTime?)m.StartTime, ct);
        var completed = scoped.Where(m =>
            m.TrainState == TrainState.Completed && m.EndTime != null
        );
        var lastSuccessfulRun = await completed.MaxAsync(m => (DateTime?)m.EndTime, ct);
        var avgMs = await completed
            .Select(m => (double?)(m.EndTime!.Value - m.StartTime).TotalMilliseconds)
            .AverageAsync(ct);

        return new TrainExecutionStats(
            trainName,
            Total: byState.Sum(x => x.Count),
            Completed: CountOf(TrainState.Completed),
            Failed: CountOf(TrainState.Failed),
            InProgress: CountOf(TrainState.InProgress),
            Pending: CountOf(TrainState.Pending),
            Cancelled: CountOf(TrainState.Cancelled),
            LastRun: lastRun,
            LastSuccessfulRun: lastSuccessfulRun,
            AverageMilliseconds: avgMs
        );
    }

    /// <summary>
    /// The processes that have executed trains, rolled up from <c>metadata</c> by
    /// <c>HostInstanceId</c>: last-seen, total executions, and how many are still running. Backs the
    /// dashboard's cluster view. This is a full aggregation over the metadata table (like the
    /// dashboard metrics), so it is meant for occasional refresh, not a hot poll.
    /// </summary>
    public async Task<IReadOnlyList<HostInfo>> GetHosts(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        // Aggregate into an anonymous shape first: a filtered COUNT and a DTO constructor inside a
        // GroupBy projection don't translate, but SUM(CASE ...) does. Order and map to HostInfo
        // client-side (the host list is tiny).
        var rows = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.HostInstanceId != null)
            .GroupBy(m => new
            {
                m.HostInstanceId,
                m.HostName,
                m.HostEnvironment,
            })
            .Select(g => new
            {
                g.Key.HostInstanceId,
                g.Key.HostName,
                g.Key.HostEnvironment,
                LastSeen = g.Max(m => m.StartTime),
                Total = g.LongCount(),
                Running = g.Sum(m => m.TrainState == TrainState.InProgress ? 1 : 0),
            })
            .ToListAsync(ct);

        return rows.OrderByDescending(r => r.LastSeen)
            .Select(r => new HostInfo(
                r.HostInstanceId!,
                r.HostName,
                r.HostEnvironment,
                r.LastSeen,
                r.Total,
                r.Running
            ))
            .ToList();
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
        long? afterId = null,
        long? manifestId = null,
        long? manifestGroupId = null,
        bool hideAdminTrains = false
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.Metadata.Metadata> filtered = db.Metadatas.AsNoTracking();

        if (trainState.HasValue)
            filtered = filtered.Where(m => m.TrainState == trainState.Value);
        if (!string.IsNullOrWhiteSpace(trainName))
            filtered = filtered.Where(m => m.Name == trainName);
        // metadata.Name stores the interface FullName (per CLAUDE.md), which is what
        // AdminTrains.FullNames holds. EF translates the list Contains to a SQL IN.
        if (hideAdminTrains)
            filtered = filtered.Where(m => !AdminTrains.FullNames.Contains(m.Name));
        if (startedAfter.HasValue)
            filtered = filtered.Where(m => m.StartTime >= startedAfter.Value);
        if (startedBefore.HasValue)
            filtered = filtered.Where(m => m.StartTime <= startedBefore.Value);
        if (manifestId.HasValue)
            filtered = filtered.Where(m => m.ManifestId == manifestId.Value);
        if (manifestGroupId.HasValue)
        {
            // Executions for a group = executions of any manifest in that group. The subquery
            // stays index-friendly: manifest.manifest_group_id is indexed, and the resulting
            // manifest ids seek ix_metadata_manifest_state on the metadata side.
            var groupManifestIds = db
                .Manifests.AsNoTracking()
                .Where(mf => mf.ManifestGroupId == manifestGroupId.Value)
                .Select(mf => (long?)mf.Id);
            filtered = filtered.Where(m => groupManifestIds.Contains(m.ManifestId));
        }

        var hasFilter =
            trainState.HasValue
            || !string.IsNullOrWhiteSpace(trainName)
            || startedAfter.HasValue
            || startedBefore.HasValue
            || manifestId.HasValue
            || manifestGroupId.HasValue
            || hideAdminTrains;

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
