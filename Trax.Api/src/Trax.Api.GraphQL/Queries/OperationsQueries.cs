using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Validation;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using ManifestExecutionStats = Trax.Api.DTOs.ManifestExecutionStats;

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
    [NamespaceField]
    public DeadLetterQueries DeadLetters() => new();

    /// <summary>
    /// Nested namespace exposing work queue queries (<c>workQueues</c>, <c>workQueue</c>).
    /// </summary>
    [NamespaceField]
    public WorkQueueQueries WorkQueue() => new();

    /// <summary>
    /// Nested namespace exposing manifest group queries (<c>graph</c>).
    /// </summary>
    [NamespaceField]
    public ManifestGroupQueries ManifestGroups() => new();

    /// <summary>
    /// Nested namespace exposing log queries (paginated reads of the log records trains write).
    /// </summary>
    [NamespaceField]
    public LogQueries Logs() => new();

    /// <summary>
    /// Nested namespace exposing dashboard / server metrics. Same data the dashboard
    /// Index page renders.
    /// </summary>
    [NamespaceField]
    public MetricsQueries Metrics() => new();

    /// <summary>
    /// Nested namespace exposing live scheduler runtime config (what the dashboard's
    /// ServerSettingsPage reads).
    /// </summary>
    [NamespaceField]
    public ConfigQueries Config() => new();

    /// <summary>
    /// The current health summary: queue depth, running and recently failed executions, and dead
    /// letters awaiting intervention. Computed on every call.
    /// </summary>
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

    /// <summary>
    /// Every train registered with this host, with its input schema, authorization requirements and
    /// how it is exposed in GraphQL.
    /// </summary>
    /// <param name="discoveryService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="hideAdminTrains">Leave out the scheduler's own internal trains (see <c>adminTrainNames</c>).</param>
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
            )
            {
                FullName = r.ServiceType.FullName!,
            })
            .ToList();
    }

    /// <summary>
    /// The observational effects registered in THIS process, with their enabled + toggleable state
    /// and, for a factory that exposes runtime settings, those settings as JSON. The registry is an
    /// in-memory per-process singleton, so this reflects the API host only, not the
    /// scheduler/worker processes where effects run; <c>operations.setEffectEnabled</c> toggles
    /// one here. Backs the dashboard effects list.
    /// </summary>
    /// <remarks>
    /// Settings can hold credentials. They are reachable only here, under the operations
    /// namespace, so they answer to the same gate as an execution's input, and a settings member
    /// marked <c>[TraxSensitive]</c> is written as <c>{"_redacted": true}</c>.
    /// </remarks>
    public IReadOnlyList<EffectInfo> GetEffects(
        [Service] IEffectRegistry registry,
        [Service] IServiceProvider services
    )
    {
        return registry
            .GetAll()
            .Select(kvp =>
            {
                var configurable = services.GetService(kvp.Key) as IConfigurableProviderFactory;
                return new EffectInfo(
                    kvp.Key.Name,
                    kvp.Key.FullName ?? kvp.Key.Name,
                    kvp.Value,
                    registry.IsToggleable(kvp.Key),
                    IsConfigurable: configurable is not null,
                    ConfigurationTypeName: configurable?.GetConfigurationType().FullName,
                    Configuration: configurable is null ? null : SerializeSettings(configurable)
                );
            })
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><see cref="SettingsJson"/> with every <c>[TraxSensitive]</c> member masked.</summary>
    private static readonly JsonSerializerOptions RedactedSettingsJson =
        TraxRedaction.WithRedaction(SettingsJson);

    // Serialized against the runtime type so a settings object typed as object still writes
    // its properties, and a [TraxSensitive] member at any depth is written as the mask, as every
    // other copy on the operations surface is. A settings type System.Text.Json cannot write
    // (a delegate, a pointer) reads as null rather than failing the whole effects list.
    private static string? SerializeSettings(IConfigurableProviderFactory factory)
    {
        var settings = factory.GetConfiguration();
        try
        {
            return JsonSerializer.Serialize(settings, settings.GetType(), RedactedSettingsJson);
        }
        catch (Exception e) when (e is NotSupportedException or JsonException)
        {
            return null;
        }
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

    /// <summary>
    /// A page of manifests, newest first. Pass the previous page's <c>nextCursor</c> as
    /// <c>afterId</c> to page deeply; <c>skip</c> is ignored when <c>afterId</c> is set. Carries no
    /// train input; <c>manifestDetail</c> does.
    /// </summary>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="sqlDialect">Resolved from DI when the provider registers one; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many manifests to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>, so page deeper with <c>afterId</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="isEnabled">Only enabled (<c>true</c>) or disabled (<c>false</c>) manifests.</param>
    /// <param name="scheduleType">Only manifests with this schedule type.</param>
    /// <param name="nameContains">Only manifests whose train name contains this text.</param>
    /// <param name="afterId">Only manifests older than this id (a keyset cursor).</param>
    /// <param name="manifestGroupId">Only manifests in this group.</param>
    public async Task<PagedResult<ManifestSummary>> GetManifests(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        bool? isEnabled = null,
        ScheduleType? scheduleType = null,
        string? nameContains = null,
        long? afterId = null,
        long? manifestGroupId = null,
        [Service] ISqlDialect? sqlDialect = null
    )
    {
        take = OperationsPageBounds.Take(take);
        skip = OperationsPageBounds.Skip(skip);

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

        // A filtered total is exact; an unfiltered one may be estimated. The cursor never
        // changes it: totalCount is the size of the whole list, whichever page this is.
        var (totalCount, isEstimate) = hasFilter
            ? (await baseQuery.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                sqlDialect,
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
                m.Priority,
                m.ManifestGroup.Name
            )
            {
                ReplayDecisionsOnRetry = m.ReplayDecisionsOnRetry,
            })
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

    /// <summary>
    /// One manifest by id, without its train input, or <c>null</c> when none has that id.
    /// </summary>
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
                m.Priority,
                m.ManifestGroup.Name
            )
            {
                ReplayDecisionsOnRetry = m.ReplayDecisionsOnRetry,
            })
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Full detail for one manifest, including the train input it runs with. The input is on this
    /// single-row read only, never on the <c>manifests</c> list, the way an execution's input is on
    /// <c>executionDetail</c> alone. The manifest keeps it unmasked because its runs start from it;
    /// here each <c>[TraxSensitive]</c> member reads <c>{"_redacted": true}</c>, and an input this
    /// host cannot read as its type is masked whole. Returns <c>null</c> when the manifest does
    /// not exist.
    /// </summary>
    public async Task<ManifestDetail?> GetManifestDetail(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        [Service] ITrainDiscoveryService discovery,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var detail = await db
            .Manifests.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ManifestDetail(
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
                m.ManifestGroup.Name,
                m.DependsOnManifestId,
                m.Priority,
                m.PropertyTypeName,
                m.Properties,
                m.MisfirePolicy,
                m.MisfireThresholdSeconds,
                m.ScheduledAt,
                m.NextScheduledRun,
                m.VarianceSeconds
            )
            {
                ReplayDecisionsOnRetry = m.ReplayDecisionsOnRetry,
            })
            .FirstOrDefaultAsync(ct);

        return detail is null
            ? null
            : detail with
            {
                Properties = TransportInputRedaction.Redact(
                    discovery,
                    detail.Properties,
                    detail.PropertyTypeName
                ),
            };
    }

    /// <summary>
    /// Execution roll-up for a single manifest: run counts by state plus the most recent run and
    /// most recent successful run. Backs the summary cards on the dashboard's manifest detail page,
    /// and reads through the same <see cref="IOperationsService"/> call. A manifest with no runs,
    /// or an id with no manifest, gets zeros and nulls.
    /// </summary>
    public async Task<ManifestExecutionStats> GetManifestStats(
        long manifestId,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var stats = await operationsService.GetManifestExecutionStatsAsync(manifestId, ct);

        return new ManifestExecutionStats(
            stats.ManifestId,
            stats.Total,
            stats.Completed,
            stats.Failed,
            stats.InProgress,
            stats.Pending,
            stats.Cancelled,
            stats.LastRun,
            stats.LastSuccessfulRun
        );
    }

    /// <summary>
    /// Execution roll-up for one train, keyed by its interface FullName (the name every
    /// execution records). Backs the per-train detail page.
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
    /// The processes that have executed trains, rolled up from the recorded executions by
    /// <c>HostInstanceId</c>: last-seen, total executions, and how many are still running. Backs the
    /// dashboard's cluster view. It aggregates over every recorded execution (like the dashboard
    /// metrics), so it is meant for occasional refresh, not a hot poll.
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

    /// <summary>
    /// A page of executions. Pass the previous page's <c>nextCursor</c> as <c>afterId</c> to page
    /// deeply in either order; <c>skip</c> is ignored when <c>afterId</c> is set. The total is exact
    /// whenever a filter is given; unfiltered, it may be an estimate. Carries no input, output or stack trace;
    /// <c>executionDetail</c> does.
    /// </summary>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="sqlDialect">Resolved from DI when the provider registers one; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many executions to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>, so page deeper with <c>afterId</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="trainState">Only executions in this state.</param>
    /// <param name="trainName">Only executions of this train (the train interface's full name, matched exactly).</param>
    /// <param name="startedAfter">Only executions that started at or after this time (UTC).</param>
    /// <param name="startedBefore">Only executions that started at or before this time (UTC).</param>
    /// <param name="order">Newest first (the default) or oldest first.</param>
    /// <param name="afterId">A keyset cursor: the page continues after this id in the chosen order.</param>
    /// <param name="manifestId">Only executions scheduled by this manifest.</param>
    /// <param name="manifestGroupId">Only executions scheduled by a manifest in this group.</param>
    /// <param name="hideAdminTrains">Leave out the scheduler's own internal trains.</param>
    /// <param name="failureClass">Only executions whose failure was classified this way.</param>
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
        bool hideAdminTrains = false,
        FailureClass? failureClass = null,
        [Service] ISqlDialect? sqlDialect = null
    )
    {
        take = OperationsPageBounds.Take(take);
        skip = OperationsPageBounds.Skip(skip);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.Metadata.Metadata> filtered = db.Metadatas.AsNoTracking();

        if (trainState.HasValue)
            filtered = filtered.Where(m => m.TrainState == trainState.Value);
        if (failureClass.HasValue)
            filtered = filtered.Where(m => m.FailureClass == failureClass.Value);
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
            || hideAdminTrains
            || failureClass.HasValue;

        // A filtered total is exact; an unfiltered one may be estimated. The cursor never
        // changes it: totalCount is the size of the whole list, whichever page this is.
        var (totalCount, isEstimate) = hasFilter
            ? (await filtered.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                sqlDialect,
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
                m.HostInstanceId,
                m.FailureClass
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

    /// <summary>
    /// One execution by id, without input, output or stack trace, or <c>null</c> when none has that id.
    /// </summary>
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
                m.HostInstanceId,
                m.FailureClass
            ))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Full detail for one execution, including its input, output, stack trace and the number of
    /// executions it started, or <c>null</c> when none has that id.
    /// </summary>
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
                m.HostInstanceId,
                // ChildCount is filled in after projection; passed explicitly only because an
                // expression tree cannot skip to a later argument by name.
                0,
                m.FailureClass,
                m.ParentId,
                m.ScheduledTime,
                m.Executor,
                m.HostLabels,
                m.ReplayDecisionsOf
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
    /// Paginated child executions of a parent: the executions it started, newest first.
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
        take = OperationsPageBounds.Take(take);

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
                m.HostInstanceId,
                m.FailureClass
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;
        return new PagedResult<ExecutionSummary>(items, totalCount, 0, take, false, nextCursor);
    }

    /// <summary>
    /// The steps of one execution, in the order it reached them, as <c>AddJunctionEvents()</c>
    /// recorded them: each junction that ran, each question a routing step asked and the track it
    /// took. Empty for an execution with none recorded, and for an id with no execution.
    /// </summary>
    /// <remarks>
    /// <para>Read through <c>JunctionRunQueries.ForRun</c>, the query the dashboard's timeline
    /// reads too. A step carries no input, output or failure message, and an answer to a question
    /// about a <c>[TraxSensitive]</c> type is never present. The recorded decider is not kept, so
    /// <c>decider</c> is always null here.</para>
    /// <para>The rows trail the live <c>onJunctionEvent</c> stream by moments. A client following
    /// a running execution subscribes first, then reads this, and keeps for each position whichever
    /// is further along.</para>
    /// </remarks>
    /// <param name="metadataId">The execution's id.</param>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="afterPosition">Only steps after this position (a keyset cursor for the next page).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    public async Task<IReadOnlyList<JunctionStep>> GetJunctionRuns(
        long metadataId,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int? afterPosition = null,
        int take = OperationsPageBounds.MaxPageSize
    )
    {
        RunIdArgument.Require(metadataId);
        take = OperationsPageBounds.Take(take);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<JunctionRun> rows = db.JunctionRuns.AsNoTracking().ForRun(metadataId);
        if (afterPosition is { } after)
            rows = rows.Where(r => r.Position > after);

        var page = await rows.Take(take).ToListAsync(ct);
        return page.Select(JunctionStep.From).ToList();
    }

    // Names and enum spellings follow the options the queue and run paths deserialize input
    // with, so a client that builds its JSON from this schema writes what the reader expects.
    private static List<InputPropertySchema> GetInputSchema(Type inputType)
    {
        var options = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        return inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            // Only Condition = Always keeps the reader from accepting a property; the other
            // conditions affect writing only.
            .Where(p =>
                p.CanRead
                && p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
                    != JsonIgnoreCondition.Always
            )
            .Select(p => new InputPropertySchema(
                JsonName(p, options),
                GetFriendlyTypeName(p.PropertyType),
                Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || !p.PropertyType.IsValueType,
                EnumValues(p.PropertyType)
            ))
            .ToList();
    }

    private static string JsonName(PropertyInfo property, JsonSerializerOptions options) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? options.PropertyNamingPolicy?.ConvertName(property.Name)
        ?? property.Name;

    private static IReadOnlyList<string>? EnumValues(Type type)
    {
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        return enumType.IsEnum ? Enum.GetNames(enumType) : null;
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
