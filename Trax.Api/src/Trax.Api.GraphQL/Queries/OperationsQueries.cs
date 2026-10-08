using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Validation;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Services.Runs;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Utils;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Utils;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.Operations;
using ManifestExecutionStats = Trax.Api.DTOs.ManifestExecutionStats;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Predefined operational queries: health, trains, manifests, manifest groups, execution
/// history, and the nested <c>deadLetters</c> namespace.
/// </summary>
public partial class OperationsQueries
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
                HasQueueSubjectKey = r.HasQueueSubjectKey,
            })
            .ToList();
    }

    /// <summary>
    /// The observational effects registered in THIS process, with their enabled + toggleable state
    /// and, for a factory that exposes runtime settings, those settings: as JSON, and as
    /// <c>fields</c> an editor can be built from. Read through
    /// <see cref="IEffectSettingsService"/>, the call the dashboard's effects page makes. The
    /// registry and each settings object are in-memory per-process singletons, so this reflects the
    /// API host only, not the scheduler/worker processes where effects run;
    /// <c>operations.setEffectEnabled</c> and <c>operations.configureEffect</c> change them here.
    /// Empty when the host registers no effect registry.
    /// </summary>
    /// <remarks>
    /// Settings can hold credentials. They are reachable only here, under the operations
    /// namespace, so they answer to the same gate as an execution's input. A settings member
    /// marked <c>[TraxSensitive]</c> is written as <c>{"_redacted": true}</c> in the JSON, and its
    /// field never carries its value.
    /// </remarks>
    public IReadOnlyList<EffectInfo> GetEffects([Service] IEffectSettingsService effectSettings) =>
        effectSettings
            .GetEffects()
            .Select(e => new EffectInfo(
                e.Name,
                e.FullName,
                e.Enabled,
                e.Toggleable,
                e.IsConfigurable,
                e.ConfigurationTypeName,
                e.Configuration
            )
            {
                Fields = e
                    .Fields.Select(f => new EffectSettingInfo(
                        f.Name,
                        f.TypeName,
                        f.Kind,
                        f.Nullable,
                        f.EnumValues,
                        f.Sensitive,
                        f.HasValue,
                        // The service never reads a sensitive value back; this keeps that true
                        // here whatever an implementation returns.
                        f.Sensitive
                            ? null
                            : f.Value,
                        f.Hint
                    ))
                    .ToList(),
            })
            .ToList();

    /// <summary>
    /// One run's recorded decisions, in the order it made them: each question it asked a decider,
    /// the answer it acted on (or refused), and the tracks routing steps took on it. Read through
    /// <see cref="IOperationsService.GetRecordedDecisionsAsync"/>, the read the dashboard's run
    /// page makes. Empty for a run that recorded none, and for an id with no run. A run's replay
    /// link is on <c>executionDetail</c> (<c>replayDecisionsOf</c>, <c>replayAbandoned</c>).
    /// </summary>
    /// <remarks>
    /// An answer to a question about a <c>[TraxSensitive]</c> type is withheld, and so is every
    /// decision after the run took a track on one; see <see cref="DecisionRecord"/>.
    /// </remarks>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="afterId">Only decisions recorded after this one (a page's <c>nextCursor</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    public async Task<DecisionPage> GetDecisions(
        long metadataId,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        long? afterId = null,
        int take = 50
    )
    {
        RunIdArgument.Require(metadataId);

        var page = await operationsService.GetRecordedDecisionsAsync(
            metadataId,
            afterId,
            OperationsPageBounds.Take(take),
            ct
        );

        return new DecisionPage(
            page.Items.Select(d => new DecisionRecord(
                    d.Id,
                    d.MetadataId,
                    d.QuestionKey,
                    d.Occurrence,
                    d.Kind,
                    d.Question,
                    d.Answer,
                    d.Refused,
                    d.IsRefused,
                    d.Fingerprint,
                    d.Model,
                    d.Decider,
                    d.Replayed,
                    d.Shadows,
                    d.Routes,
                    d.StateHash,
                    d.DecidedAt,
                    d.AnswerWithheld,
                    d.TrackWithheld
                )
                {
                    ReplayRefused = ReplayRefusedOf(d.Answer),
                })
                .ToList(),
            page.Take,
            page.NextCursor
        );
    }

    /// <summary>
    /// The <c>replay_refused</c> member of a recorded answer, or <c>null</c> when it has none or
    /// is not a JSON object.
    /// </summary>
    private static string? ReplayRefusedOf(string? answer)
    {
        if (string.IsNullOrEmpty(answer))
            return null;
        try
        {
            using var json = JsonDocument.Parse(answer);
            return
                json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("replay_refused", out var refused)
                && refused.ValueKind == JsonValueKind.String
                ? refused.GetString()
                : null;
        }
        catch (JsonException)
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
    /// <param name="hideAdminTrains">Leave out the scheduler's own internal trains' manifests (see <c>adminTrainNames</c>).</param>
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
        bool hideAdminTrains = false,
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
        // A manifest's Name is its train's interface FullName, the form AdminTrains.FullNames
        // holds; the dashboard's manifests page hides the same rows.
        if (hideAdminTrains)
            baseQuery = baseQuery.Where(m => !AdminTrains.FullNames.Contains(m.Name));

        var hasFilter =
            isEnabled.HasValue
            || scheduleType.HasValue
            || !string.IsNullOrWhiteSpace(nameContains)
            || manifestGroupId.HasValue
            || hideAdminTrains;

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
    /// deeply in either order; <c>skip</c> is ignored when <c>afterId</c> is set. Unfiltered, the
    /// total may be an estimate (<c>isEstimatedCount</c>). Filtered by failure text, it counts at
    /// most 10,000 matches: past that it reads 10,000 with <c>isCountCapped</c> true, a lower bound.
    /// Under any other filter it is exact. Carries no input, output or stack trace;
    /// <c>executionDetail</c> does.
    /// </summary>
    /// <remarks>
    /// <c>failureReasonContains</c> matches text anywhere in the failure reason, ignoring case,
    /// with <c>%</c>, <c>_</c> and <c>\</c> matching themselves. On Postgres a term of three
    /// characters or more is looked up in a trigram index; a shorter one reads the run table.
    /// </remarks>
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
    /// <param name="externalId">Only the execution with this external id (matched exactly).</param>
    /// <param name="parentId">Only executions started from inside this execution's run.</param>
    /// <param name="hostName">Only executions run on the machine with this name (matched exactly).</param>
    /// <param name="failureReasonContains">Only executions whose failure reason contains this text, ignoring case.</param>
    /// <param name="failureJunction">Only executions that failed in the junction with this name (matched exactly).</param>
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
        string? externalId = null,
        long? parentId = null,
        string? hostName = null,
        string? failureReasonContains = null,
        string? failureJunction = null,
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
        // Each of these three seeks an index of its own: ix_metadata_external_id,
        // ix_metadata_parent_id and ix_metadata_host_name.
        if (!string.IsNullOrWhiteSpace(externalId))
            filtered = filtered.Where(m => m.ExternalId == externalId);
        if (parentId.HasValue)
            filtered = filtered.Where(m => m.ParentId == parentId.Value);
        if (!string.IsNullOrWhiteSpace(hostName))
            filtered = filtered.Where(m => m.HostName == hostName);
        // ix_metadata_failure_junction holds only the runs that failed, in id order under each
        // junction, so a page of one junction's failures reads just that page.
        if (!string.IsNullOrWhiteSpace(failureJunction))
            filtered = filtered.Where(m => m.FailureJunction == failureJunction);
        // Lowered, with the term's wildcards escaped: the expression
        // ix_metadata_failure_reason_trgm is built over, so a rare term reads only its matches.
        var textFilter = !string.IsNullOrEmpty(failureReasonContains);
        if (textFilter)
        {
            var pattern = LikePattern.Contains(failureReasonContains!);
            filtered = filtered.Where(m =>
                EF.Functions.Like(m.FailureReason!.ToLower(), pattern, LikePattern.Escape)
            );
        }
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
            || failureClass.HasValue
            || !string.IsNullOrWhiteSpace(externalId)
            || parentId.HasValue
            || !string.IsNullOrWhiteSpace(hostName)
            || !string.IsNullOrWhiteSpace(failureJunction)
            || textFilter;

        // A text filter counts up to FailureTextCountCap matches and no further, so a common term
        // never counts every failed run; every other filtered total is exact, and an unfiltered
        // one may be estimated. The cursor never changes it: totalCount is the size of the whole
        // list, whichever page this is.
        int totalCount;
        var isEstimate = false;
        var isCapped = false;
        if (textFilter)
        {
            // One match past the cap tells a capped count from an exact one, and the database
            // stops reading once it has found that many.
            var count = await filtered.Take(FailureTextCountCap + 1).CountAsync(ct);
            (totalCount, isCapped) =
                count > FailureTextCountCap ? (FailureTextCountCap, true) : (count, false);
        }
        else if (hasFilter)
            totalCount = await filtered.CountAsync(ct);
        else
            (totalCount, isEstimate) = await CountEstimator.EstimateOrCountAsync(
                db,
                sqlDialect,
                "metadata",
                () => filtered.CountAsync(ct),
                ct
            );

        var oldest = order == SortOrder.Oldest;
        var items =
            textFilter && (afterId.HasValue || skip == 0)
                ? await ReadTextFilteredExecutionsAsync(db, filtered, afterId, oldest, take, ct)
                : await ReadExecutionsInOrderAsync(filtered, afterId, oldest, skip, take, ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<ExecutionSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        )
        {
            IsCountCapped = isCapped,
        };
    }

    /// <summary>
    /// The most matches <see cref="GetExecutions"/> counts under a failure text filter: the log
    /// list's cap, so the two text searches count alike.
    /// </summary>
    internal const int FailureTextCountCap = OperationsService.LogCountCap;

    /// <summary>
    /// How many ids a page filtered by failure text reads in id order from where it starts, before
    /// it finds the rest of its matches through the trigram index instead. The log list's text
    /// read takes the same two steps over the same width.
    /// </summary>
    /// <remarks>
    /// In id order alone, a term found only in runs far from where the page starts makes the
    /// database walk every run in between, matching or not. The window fills the page when the
    /// term is common near the start, and reading it whole costs a few milliseconds when the term
    /// is not there; a term rarer than that has few matches, which are cheap to find through the
    /// index and sort.
    /// </remarks>
    internal const int FailureTextWindow = 10_000;

    private static async Task<List<ExecutionSummary>> ReadExecutionsInOrderAsync(
        IQueryable<Effect.Models.Metadata.Metadata> filtered,
        long? afterId,
        bool oldest,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        // Keyset stays safe in both directions: Newest pages id < afterId (DESC), Oldest
        // pages id > afterId (ASC). Both use the primary key index.
        var query = filtered;
        if (afterId.HasValue)
            query = oldest
                ? query.Where(m => m.Id > afterId.Value)
                : query.Where(m => m.Id < afterId.Value);
        query = oldest ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id);

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        return await query.Take(take).Select(ToExecutionSummary).ToListAsync(ct);
    }

    /// <summary>
    /// A page filtered by failure text, read in two steps (see <see cref="FailureTextWindow"/>).
    /// The rows and their order are the same as one read in id order.
    /// </summary>
    private static async Task<List<ExecutionSummary>> ReadTextFilteredExecutionsAsync(
        Trax.Effect.Data.Services.DataContext.IDataContext db,
        IQueryable<Effect.Models.Metadata.Metadata> filtered,
        long? afterId,
        bool oldest,
        int take,
        CancellationToken ct
    )
    {
        // The id the page reads away from: the cursor, or just past the end of the table it starts at.
        var start =
            afterId
            ?? (
                oldest
                    ? await db.Metadatas.MinAsync(m => (long?)m.Id, ct) - 1
                    : await db.Metadatas.MaxAsync(m => (long?)m.Id, ct) + 1
            );
        if (start is not { } origin)
            return [];

        var edge = oldest ? origin + FailureTextWindow : origin - FailureTextWindow;
        var near = oldest
            ? filtered.Where(m => m.Id > origin && m.Id <= edge).OrderBy(m => m.Id)
            : filtered.Where(m => m.Id < origin && m.Id >= edge).OrderByDescending(m => m.Id);
        var page = await near.Take(take).Select(ToExecutionSummary).ToListAsync(ct);
        if (page.Count == take)
            return page;

        // "+ 0" keeps the primary key from serving the order, so what is sorted is the matches.
        var far = oldest
            ? filtered.Where(m => m.Id > edge).OrderBy(m => m.Id + 0)
            : filtered.Where(m => m.Id < edge).OrderByDescending(m => m.Id + 0);
        page.AddRange(await far.Take(take - page.Count).Select(ToExecutionSummary).ToListAsync(ct));
        return page;
    }

    private static readonly System.Linq.Expressions.Expression<
        Func<Effect.Models.Metadata.Metadata, ExecutionSummary>
    > ToExecutionSummary = m => new ExecutionSummary(
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
    )
    {
        ParentId = m.ParentId,
        CurrentlyRunningJunction = m.CurrentlyRunningJunction,
    };

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
            )
            {
                ParentId = m.ParentId,
                CurrentlyRunningJunction = m.CurrentlyRunningJunction,
            })
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
            )
            {
                ReplayAbandoned = m.ReplayAbandoned,
                ResumeFrom = m.ResumeFrom,
                ResumeAt = m.ResumeAt,
            })
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
            )
            {
                ParentId = m.ParentId,
                CurrentlyRunningJunction = m.CurrentlyRunningJunction,
            })
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

    /// <summary>
    /// The declared chain of the registered train named <paramref name="train"/>, its canonical
    /// name (the interface's full name, as <c>execution.name</c> carries it), as a graph: every
    /// step in order, each routing step's tracks, and a hash that changes with the chain. Null when
    /// no registered train has that name, or its chain cannot be read outside a request.
    /// </summary>
    /// <remarks>
    /// Only registered trains are looked up, by name, so no name a caller sends makes the host load
    /// a type. Read through <see cref="ITrainChainGraphs"/>, which the dashboard's run page reads
    /// too. The graph names the train's types, so it is under the operations gate.
    /// </remarks>
    /// <param name="train">The train's canonical name.</param>
    /// <param name="chainGraphs">Resolved from DI; not a GraphQL argument.</param>
    public ChainGraph? GetDeclaredChain(string train, [Service] ITrainChainGraphs chainGraphs) =>
        chainGraphs.Find(train);

    /// <summary>
    /// One execution drawn on its train's declared chain: each node with the steps the run
    /// recorded for it and where it stands (completed, failed, skipped on a track not taken, not
    /// reached), the track each routing step took, and the steps that match no node. Null for an
    /// id with no execution.
    /// </summary>
    /// <remarks>
    /// Read through <c>RunGraphs.ReadAsync</c>, the read the dashboard's run graph makes.
    /// Steps are matched by node id; a step recorded without one, or for a node the current chain
    /// no longer declares, is in <c>unmatchedSteps</c>. At most the first 500 steps are read
    /// (<c>moreSteps</c> says when there were more). For a failed or cancelled execution, and a
    /// resumed one, the checkpoints it can resume from are read too, once per graph: each node
    /// says whether <c>resumeExecution</c> can resume there (<c>canResume</c>) and whether a
    /// checkpoint is stored at it (<c>checkpointed</c>), and a resumed execution's nodes before its
    /// resume point are <c>RESTORED</c>. What a checkpoint holds is never returned.
    /// </remarks>
    /// <param name="metadataId">The execution's id.</param>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="chainGraphs">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="resumes">Resolved from DI; not a GraphQL argument. Without it no resume is offered.</param>
    public async Task<RunGraph?> GetRunGraph(
        long metadataId,
        [Service] IDataContextProviderFactory dataContextFactory,
        [Service] ITrainChainGraphs chainGraphs,
        CancellationToken ct,
        [Service] IRunResumes? resumes = null
    )
    {
        RunIdArgument.Require(metadataId);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);
        return await RunGraphs.ReadAsync(db, chainGraphs, resumes, metadataId, ct);
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
