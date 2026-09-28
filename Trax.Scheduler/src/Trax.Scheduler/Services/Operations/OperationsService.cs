using System.Data.Common;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Utils;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Services.Operations;

/// <inheritdoc />
public class OperationsService : IOperationsService
{
    private readonly ITrainDiscoveryService _discoveryService;
    private readonly IDataContextProviderFactory _dataContextFactory;
    private readonly SchedulerConfiguration _schedulerConfiguration;
    private readonly LocalWorkerOptions? _localWorkerOptions;
    private readonly ITraxChangeSignal? _changeSignal;
    private readonly ITrainExecutionService _trainExecution;
    private readonly ILogger<OperationsService>? _logger;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// The constructor dependency injection uses. The service provider is the scope's own, and
    /// <see cref="RunTrainAsync"/> resolves from it what only a run needs: the job submitter the
    /// train is routed to, and the authorization services the mediator would consult.
    /// </summary>
    public OperationsService(
        ITrainDiscoveryService discoveryService,
        IDataContextProviderFactory dataContextFactory,
        SchedulerConfiguration schedulerConfiguration,
        ITrainExecutionService trainExecution,
        IServiceProvider services,
        LocalWorkerOptions? localWorkerOptions = null,
        ITraxChangeSignal? changeSignal = null,
        ILogger<OperationsService>? logger = null
    )
        : this(
            discoveryService,
            dataContextFactory,
            schedulerConfiguration,
            trainExecution,
            localWorkerOptions,
            changeSignal,
            logger
        )
    {
        _services = services;
    }

    /// <summary>
    /// Kept so code constructing the service directly still compiles. A service built this way
    /// has no service provider, so <see cref="RunTrainAsync"/> refuses to run.
    /// </summary>
    public OperationsService(
        ITrainDiscoveryService discoveryService,
        IDataContextProviderFactory dataContextFactory,
        SchedulerConfiguration schedulerConfiguration,
        // Required, not optional: enqueueing goes through it so that train authorization,
        // the OnQueue hook and the subject key all apply. A fallback path here would be a
        // second way to enqueue that skips all three.
        ITrainExecutionService trainExecution,
        // LocalWorkerOptions is only registered when UseLocalWorkers() is called; treat as optional.
        LocalWorkerOptions? localWorkerOptions = null,
        // Optional so direct construction in tests stays simple; always resolved via DI in a host.
        ITraxChangeSignal? changeSignal = null,
        // Optional for the same reason; a host always has logging.
        ILogger<OperationsService>? logger = null
    )
    {
        _logger = logger;
        _discoveryService = discoveryService;
        _dataContextFactory = dataContextFactory;
        _schedulerConfiguration = schedulerConfiguration;
        _localWorkerOptions = localWorkerOptions;
        _changeSignal = changeSignal;
        _trainExecution = trainExecution;
    }

    /// <summary>
    /// The constructor as it shipped before the logger parameter, kept so that an assembly built
    /// against it still binds. It takes no defaults, so a call that leaves the optional
    /// parameters out resolves to the constructor above rather than being ambiguous.
    /// </summary>
    public OperationsService(
        ITrainDiscoveryService discoveryService,
        IDataContextProviderFactory dataContextFactory,
        SchedulerConfiguration schedulerConfiguration,
        ITrainExecutionService trainExecution,
        LocalWorkerOptions? localWorkerOptions,
        ITraxChangeSignal? changeSignal
    )
        : this(
            discoveryService,
            dataContextFactory,
            schedulerConfiguration,
            trainExecution,
            localWorkerOptions,
            changeSignal,
            logger: null
        ) { }

    /// <inheritdoc />
    public async Task<OperationResult> QueueTrainAsync(QueueTrainInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.TrainName))
            return new OperationResult(false, Message: "TrainName is required.");

        // Compare against the interface FullName per CLAUDE.md naming rules.
        // `ServiceTypeName` is a friendly name (e.g. "IServiceTrain<X, Y>") and is not
        // suitable for an exact match.
        var registration = _discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r => r.ServiceType.FullName == input.TrainName);

        if (registration is null)
            return new OperationResult(
                false,
                Message: $"Unknown train: {input.TrainName}. Use operations.getTrains to list registered trains."
            );

        // Enqueue through the mediator rather than writing the row here. That is what applies
        // the train's [TraxAuthorize] requirements, fires OnQueue, and stamps the subject key,
        // none of which a hand-built entry got. The input is handed over unparsed: the mediator
        // authorizes before it reads it, so a caller who may not run the train learns nothing
        // about the input it expects. A TrainAuthorizationException propagates rather than being
        // flattened into a failed OperationResult: not being allowed to run something is not a
        // validation outcome.
        QueueTrainResult queued;

        try
        {
            queued = await _trainExecution.QueueAsync(
                registration.ServiceType.FullName!,
                input.InputJson,
                input.Priority,
                input.ScheduledAt,
                ct
            );
        }
        catch (JsonException ex)
        {
            return new OperationResult(false, Message: $"Invalid InputJson: {ex.Message}");
        }
        catch (TrainInputValidationException ex)
        {
            // Generic by design: the cap and the observed size are on the exception's properties,
            // not in its message, so the caller cannot map the cap. Trax.Api's error filter makes
            // the same promise for the typed exception.
            return new OperationResult(false, Message: ex.Message);
        }
        catch (Exception ex)
            when (ex is not UnauthorizedAccessException and not OperationCanceledException
                && IsInfrastructureFailure(ex)
            )
        {
            // The server failed, not the train: the database or the network the enqueue depends
            // on. Not a refusal, so it is not reported as one, and its message (a connection
            // string's host and port, a constraint name) is not handed to the caller. It is
            // logged here and rethrown, the way every other operation lets a data failure
            // through; the GraphQL error filter masks it. See scheduler/0004.
            _logger?.LogError(
                ex,
                "Queueing {TrainName} failed on infrastructure, not on a refusal",
                registration.ServiceType.FullName
            );
            throw;
        }
        catch (Exception ex)
            when (ex is not UnauthorizedAccessException and not OperationCanceledException)
        {
            // A refusal: the train's OnQueue hook or QueueSubjectKey threw, the subject key could
            // not be used, or a deferred entry was cancelled before it was confirmed. The message
            // is the train author's or the mediator's, written for the caller. The mediator's
            // missing-enforcer InvalidOperationException also lands here, because nothing tells
            // it apart from a subject key refusal but its text.
            return new OperationResult(false, Message: $"The enqueue was refused: {ex.Message}");
        }

        _changeSignal?.Notify(ChangeDomain.WorkQueue);

        return new OperationResult(
            true,
            Id: queued.WorkQueueId,
            Count: 1,
            Message: $"Work queue entry {queued.WorkQueueId} created."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> RunTrainAsync(RunTrainInput input, CancellationToken ct)
    {
        var services =
            _services
            ?? throw new InvalidOperationException(
                "This OperationsService was constructed without an IServiceProvider, so it cannot "
                    + "resolve a job submitter. Resolve IOperationsService from dependency injection, "
                    + "or use the constructor that takes one."
            );

        if (string.IsNullOrWhiteSpace(input.TrainName))
            return new OperationResult(false, Message: "TrainName is required.");

        // The same lookup, and the same answer for a miss, as QueueTrainAsync.
        var registration = _discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r => r.ServiceType.FullName == input.TrainName);

        if (registration is null)
            return new OperationResult(
                false,
                Message: $"Unknown train: {input.TrainName}. Use operations.getTrains to list registered trains."
            );

        var trainName = registration.ServiceType.FullName!;

        // Authorize before the input is read, as the mediator does for a queue, so a caller who
        // may not run the train learns nothing about its input from a parse error. An
        // UnauthorizedAccessException, and the missing-enforcer InvalidOperationException, are
        // not caught: neither is an answer about this run.
        await AuthorizeRunAsync(services, registration, ct);

        object runInput;

        try
        {
            runInput = ReadRunInput(services, registration, input.InputJson);
        }
        catch (JsonException ex)
        {
            return new OperationResult(false, Message: $"Invalid InputJson: {ex.Message}");
        }
        catch (TrainInputValidationException ex)
        {
            // Generic by design: the cap and the observed size are on the exception's properties,
            // not in its message, so the caller cannot map the cap. Trax.Api's error filter makes
            // the same promise for the typed exception.
            return new OperationResult(false, Message: ex.Message);
        }

        // The row the run reports on. Input stays null here, as the job dispatcher leaves it:
        // the run's own effects record the input when the train starts.
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        using (var db = await _dataContextFactory.CreateDbContextAsync(ct))
        {
            await db.Track(metadata);
            await db.SaveChanges(ct);
        }

        try
        {
            await ResolveSubmitter(services, trainName).EnqueueAsync(metadata.Id, runInput, ct);
        }
        catch (Exception ex)
        {
            // No job exists to move the row out of Pending, so fail it now with the submitter's
            // exception, as the job dispatcher does when a dispatch fails, rather than leave it
            // for the stale-pending reaper to fail later for the wrong reason. The failure is the
            // server's, not a refusal, so it is thrown (scheduler/0004).
            _logger?.LogError(
                ex,
                "Submitting a run of {TrainName} (metadata {MetadataId}) failed",
                trainName,
                metadata.Id
            );
            await FailUnsubmittedRunAsync(metadata.Id, ex);
            throw;
        }

        return new OperationResult(
            true,
            Id: metadata.Id,
            Count: 1,
            Message: $"Run {metadata.Id} of {trainName} submitted."
        );
    }

    /// <summary>
    /// The mediator's authorization rule for an enqueue, applied to a run: an enforcer, when one
    /// is registered, decides (and honours a trusted scope itself); otherwise a trusted scope
    /// passes, and a <c>[TraxAuthorize]</c> train fails closed unless the host opted out
    /// (mediator/0001). A copy, because the published mediator keeps its check private.
    /// </summary>
    private static async Task AuthorizeRunAsync(
        IServiceProvider services,
        TrainRegistration registration,
        CancellationToken ct
    )
    {
        var authorization = services.GetService<ITrainAuthorizationService>();

        if (authorization is not null)
        {
            await authorization.AuthorizeAsync(registration, ct);
            return;
        }

        if (services.GetService<ITrustedExecutionScope>() is { IsTrusted: true })
            return;

        var allowMissing =
            services.GetService<MediatorConfiguration>()?.AllowMissingAuthorizationService ?? false;

        if (registration.HasAuthorizeAttribute && !allowMissing)
            throw new InvalidOperationException(
                $"Train '{registration.ServiceTypeName}' declares [TraxAuthorize] but no "
                    + "ITrainAuthorizationService is registered. Call AddTraxApi() (or register "
                    + "a custom ITrainAuthorizationService) before building the host. If this "
                    + "process intentionally runs no authorized submissions, opt out with "
                    + "AddMediator(m => m.AllowMissingAuthorizationService())."
            );
    }

    /// <summary>
    /// Reads a run's input the way the mediator reads a queued one: the input size cap, property
    /// names matched whatever their case and a property given twice refused (docs/0023), a blank
    /// input standing for an empty object that the input type must be buildable from, and a JSON
    /// <c>null</c> refused. A copy, for the same reason as <see cref="AuthorizeRunAsync"/>;
    /// keeping it identical is what makes a run and a queue of the same JSON agree.
    /// </summary>
    private static object ReadRunInput(
        IServiceProvider services,
        TrainRegistration registration,
        string? inputJson
    )
    {
        var missing = string.IsNullOrWhiteSpace(inputJson);
        var json = missing ? "{}" : inputJson!;

        var maxBytes =
            services.GetService<MediatorConfiguration>()?.MaxInputJsonBytes
            ?? new MediatorConfiguration().MaxInputJsonBytes;
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        if (byteCount > maxBytes)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                maxBytes
            );

        var options = RunInputOptions();
        object? read;

        if (missing)
        {
            try
            {
                read = JsonSerializer.Deserialize(json, registration.InputType, options.Missing);
            }
            catch (JsonException refused)
            {
                throw new JsonException(
                    $"No input was given, and {registration.InputTypeName} cannot be built "
                        + $"without one: {refused.Message}",
                    refused
                );
            }
        }
        else
        {
            read = JsonSerializer.Deserialize(json, registration.InputType, options.Given);
        }

        return read
            ?? throw new JsonException(
                $"InputJson deserialized to null. Expected an instance of {registration.InputTypeName}."
            );
    }

    private static CallerInputOptions? _runInputOptions;

    /// <summary>
    /// The system options with property names matched whatever their case and a property given
    /// twice, in any casing, refused (docs/0023); the missing-input reading also respects
    /// required constructor parameters. Rebuilt only if the system options object itself is
    /// replaced, as the mediator's copy is.
    /// </summary>
    private static CallerInputOptions RunInputOptions()
    {
        var source = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        var cached = _runInputOptions;

        if (cached is not null && ReferenceEquals(cached.Source, source))
            return cached;

        var given = new JsonSerializerOptions(source)
        {
            PropertyNameCaseInsensitive = true,
            AllowDuplicateProperties = false,
        };
        var missing = new JsonSerializerOptions(given)
        {
            RespectRequiredConstructorParameters = true,
        };

        var built = new CallerInputOptions(source, given, missing);
        _runInputOptions = built;
        return built;
    }

    private sealed record CallerInputOptions(
        JsonSerializerOptions Source,
        JsonSerializerOptions Given,
        JsonSerializerOptions Missing
    );

    /// <summary>
    /// The submitter the job dispatcher would use for this train: its builder or
    /// <c>[TraxRemote]</c> route when it has one, otherwise the default <see cref="IJobSubmitter"/>.
    /// </summary>
    private static IJobSubmitter ResolveSubmitter(IServiceProvider services, string trainName)
    {
        var routed = services
            .GetService<JobSubmitterRoutingConfiguration>()
            ?.GetSubmitterType(trainName);

        return routed is not null
            ? (IJobSubmitter)services.GetRequiredService(routed)
            : services.GetRequiredService<IJobSubmitter>();
    }

    /// <summary>
    /// Fails a run whose job was never submitted. Bookkeeping for a run that already failed, so
    /// it is written on <see cref="CancellationToken.None"/>: a cancelled caller is one of the
    /// ways to get here. A failure to write it is logged and dropped, so the caller still sees
    /// why the submit failed; the stale-pending reaper fails the row later.
    /// </summary>
    private async Task FailUnsubmittedRunAsync(long metadataId, Exception submitFailure)
    {
        try
        {
            using var db = await _dataContextFactory.CreateDbContextAsync(CancellationToken.None);
            var metadata = await db.Metadatas.FirstOrDefaultAsync(m => m.Id == metadataId);
            if (metadata is null || metadata.TrainState != TrainState.Pending)
                return;

            metadata.TrainState = TrainState.Failed;
            metadata.EndTime = DateTime.UtcNow;
            metadata.AddException(submitFailure);
            await db.SaveChanges(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Could not mark unsubmitted run {MetadataId} failed; the stale-pending reaper will",
                metadataId
            );
        }
    }

    /// <summary>
    /// Whether an exception from the enqueue is the infrastructure failing rather than the
    /// enqueue being refused: a database, EF Core, network, I/O or timeout failure anywhere in
    /// its chain. The chain is walked because a hook that wraps what it caught, and EF Core
    /// wrapping the provider, both leave the cause below the outermost type. See scheduler/0004.
    /// </summary>
    internal static bool IsInfrastructureFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (
                current
                is DbException
                    or DbUpdateException
                    or TimeoutException
                    or SocketException
                    or HttpRequestException
                    or IOException
            )
                return true;

            if (
                current is AggregateException aggregate
                && aggregate.InnerExceptions.Any(IsInfrastructureFailure)
            )
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct)
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        var entry = await db.WorkQueues.FirstOrDefaultAsync(q => q.Id == id, ct);

        if (entry is null)
            return new OperationResult(false, Message: $"Work queue entry {id} not found.");

        if (entry.Status != WorkQueueStatus.Queued)
            return new OperationResult(
                false,
                Id: id,
                Message: $"Cannot cancel entry {id} with status '{entry.Status}'."
            );

        entry.Status = WorkQueueStatus.Cancelled;
        await db.SaveChanges(ct);
        _changeSignal?.Notify(ChangeDomain.WorkQueue);

        return new OperationResult(
            true,
            Id: id,
            Count: 1,
            Message: $"Work queue entry {id} cancelled."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdateManifestGroupAsync(
        long id,
        UpdateManifestGroupInput input,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        var group = await db.ManifestGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

        if (group is null)
            return new OperationResult(false, Message: $"Manifest group {id} not found.");

        if (ValidateManifestGroupPatch(input) is { } refusal)
            return new OperationResult(
                false,
                Id: id,
                Message: $"Manifest group {id} not updated: {refusal}"
            );

        var changed = 0;

        if (input.ClearMaxActiveJobs)
        {
            if (group.MaxActiveJobs is not null)
            {
                group.MaxActiveJobs = null;
                changed++;
            }
        }
        else if (input.MaxActiveJobs is { } max && group.MaxActiveJobs != max)
        {
            group.MaxActiveJobs = max;
            changed++;
        }

        if (input.Priority is { } priority && group.Priority != priority)
        {
            group.Priority = priority;
            changed++;
        }

        if (input.IsEnabled is { } enabled && group.IsEnabled != enabled)
        {
            group.IsEnabled = enabled;
            changed++;
        }

        if (changed == 0)
            return new OperationResult(
                true,
                Id: id,
                Count: 0,
                Message: $"Manifest group {id}: no changes."
            );

        group.UpdatedAt = DateTime.UtcNow;
        await db.SaveChanges(ct);
        _changeSignal?.Notify(ChangeDomain.ManifestGroup);

        return new OperationResult(
            true,
            Id: id,
            Count: changed,
            Message: $"Manifest group {id}: {changed} field(s) updated."
        );
    }

    /// <inheritdoc />
    public async Task<ManifestGroupDependencyGraph?> GetManifestGroupDependencyGraphAsync(
        long groupId,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        // Confirm the focal group exists. Returning null on missing group lets GraphQL
        // surface "not found" cleanly without throwing.
        var focalGroup = await db
            .ManifestGroups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.Name })
            .FirstOrDefaultAsync(ct);

        if (focalGroup is null)
            return null;

        var currentManifestIdsQuery = db
            .Manifests.Where(m => m.ManifestGroupId == groupId)
            .Select(m => m.Id);

        // Empty group: still return a single-node graph so the UI can render the focal node.
        if (!await currentManifestIdsQuery.AnyAsync(ct))
            return new ManifestGroupDependencyGraph(
                new[] { new DependencyGraphNode(focalGroup.Id, focalGroup.Name, true) },
                Array.Empty<DependencyGraphEdge>()
            );

        // Upstream: groups containing manifests our manifests depend on.
        var upstreamGroupIds = await db
            .Manifests.AsNoTracking()
            .Where(m => m.ManifestGroupId == groupId && m.DependsOnManifestId != null)
            .Join(
                db.Manifests.AsNoTracking(),
                dependent => dependent.DependsOnManifestId,
                parent => (long?)parent.Id,
                (dependent, parent) => parent.ManifestGroupId
            )
            .Where(parentGroupId => parentGroupId != groupId)
            .Distinct()
            .ToListAsync(ct);

        // Downstream: groups containing manifests that depend on our manifests.
        var downstreamGroupIds = await db
            .Manifests.AsNoTracking()
            .Where(m =>
                m.DependsOnManifestId != null
                && currentManifestIdsQuery.Contains(m.DependsOnManifestId.Value)
                && m.ManifestGroupId != groupId
            )
            .Select(m => m.ManifestGroupId)
            .Distinct()
            .ToListAsync(ct);

        var neighborGroupIds = upstreamGroupIds.Union(downstreamGroupIds).ToHashSet();
        var allRelevantGroupIds = neighborGroupIds.Append(groupId).ToList();

        var groups = await db
            .ManifestGroups.AsNoTracking()
            .Where(g => allRelevantGroupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(ct);

        var nodes = groups
            .Select(g => new DependencyGraphNode(g.Id, g.Name, IsHighlighted: g.Id == groupId))
            .ToList();

        // Cross-group edges only.
        var crossGroupEdges = await db
            .Manifests.AsNoTracking()
            .Where(m =>
                m.DependsOnManifestId != null && allRelevantGroupIds.Contains(m.ManifestGroupId)
            )
            .Join(
                db.Manifests.AsNoTracking(),
                dependent => dependent.DependsOnManifestId,
                parent => (long?)parent.Id,
                (dependent, parent) =>
                    new
                    {
                        ParentGroupId = parent.ManifestGroupId,
                        DependentGroupId = dependent.ManifestGroupId,
                    }
            )
            .Where(e =>
                e.ParentGroupId != e.DependentGroupId
                && allRelevantGroupIds.Contains(e.ParentGroupId)
            )
            .Distinct()
            .ToListAsync(ct);

        var edges = crossGroupEdges
            .Select(e => new DependencyGraphEdge(e.ParentGroupId, e.DependentGroupId))
            .ToList();

        return new ManifestGroupDependencyGraph(nodes, edges);
    }

    /// <inheritdoc />
    public async Task<ManifestGroupDependencyGraph> GetGlobalManifestGroupGraphAsync(
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        // Every group is a node; nothing is focal on the global view.
        var nodes = await db
            .ManifestGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new DependencyGraphNode(g.Id, g.Name, false))
            .ToListAsync(ct);

        // Cross-group edges: a manifest in one group depends on a manifest in another. Same shape as
        // the per-group query, but unbounded (all groups) and with no focal filter.
        var crossGroupEdges = await db
            .Manifests.AsNoTracking()
            .Where(m => m.DependsOnManifestId != null)
            .Join(
                db.Manifests.AsNoTracking(),
                dependent => dependent.DependsOnManifestId,
                parent => (long?)parent.Id,
                (dependent, parent) =>
                    new
                    {
                        ParentGroupId = parent.ManifestGroupId,
                        DependentGroupId = dependent.ManifestGroupId,
                    }
            )
            .Where(e => e.ParentGroupId != e.DependentGroupId)
            .Distinct()
            .ToListAsync(ct);

        var edges = crossGroupEdges
            .Select(e => new DependencyGraphEdge(e.ParentGroupId, e.DependentGroupId))
            .ToList();

        return new ManifestGroupDependencyGraph(nodes, edges);
    }

    /// <inheritdoc />
    public async Task<DashboardMetrics> GetDashboardMetricsAsync(
        MetricsRange range,
        bool hideAdminTrains,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var last7d = now.AddDays(-7);

        var adminNames = AdminTrains.FullNames.ToHashSet();

        IQueryable<Effect.Models.Metadata.Metadata> ScopedMetadatas() =>
            hideAdminTrains
                ? db.Metadatas.AsNoTracking().Where(m => !adminNames.Contains(m.Name))
                : db.Metadatas.AsNoTracking();

        // ── KPIs (today) ─────────────────────────────────────────────────────
        var todayStateCounts = await ScopedMetadatas()
            .Where(m => m.StartTime >= todayStart)
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountForState(TrainState s) =>
            todayStateCounts.FirstOrDefault(x => x.State == s)?.Count ?? 0;

        var executionsToday = todayStateCounts.Sum(x => x.Count);
        var completed = CountForState(TrainState.Completed);
        var terminal = completed + CountForState(TrainState.Failed);
        var successRate = terminal > 0 ? Math.Round(100.0 * completed / terminal, 1) : 0;

        var currentlyRunning = await ScopedMetadatas()
            .Where(m => m.TrainState == TrainState.InProgress)
            .CountAsync(ct);

        var unresolvedDeadLetters = await db
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, ct);

        var kpis = new DashboardKpis(
            executionsToday,
            successRate,
            currentlyRunning,
            unresolvedDeadLetters
        );

        // ── Executions over time ─────────────────────────────────────────────
        var executions = await BuildExecutionsOverTimeAsync(db, range, hideAdminTrains, now, ct);

        // ── Top failures (7d) ────────────────────────────────────────────────
        // EF can't construct positional records server-side; project to an anonymous
        // type, then materialise to TrainFailureCount.
        var topFailures = (
            await ScopedMetadatas()
                .Where(m => m.TrainState == TrainState.Failed && m.StartTime >= last7d)
                .GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct)
        )
            .Select(x => new TrainFailureCount(x.Name, x.Count))
            .ToList();

        // ── Top average durations (7d, root-level only) ──────────────────────
        var topDurations = (
            await ScopedMetadatas()
                .Where(m =>
                    m.TrainState == TrainState.Completed
                    && m.EndTime != null
                    && m.StartTime >= last7d
                    && m.ParentId == null
                )
                .GroupBy(m => m.Name)
                .Select(g => new
                {
                    Name = g.Key,
                    AvgMs = g.Average(m => (m.EndTime!.Value - m.StartTime).TotalMilliseconds),
                })
                .OrderByDescending(x => x.AvgMs)
                .Take(10)
                .ToListAsync(ct)
        )
            .Select(x => new TrainAverageDuration(x.Name, x.AvgMs))
            .ToList();

        // ── Throughput sparklines (7d, top 3 + Other, 28 6h buckets) ─────────
        var throughputSeries = await BuildThroughputSeriesAsync(
            db,
            hideAdminTrains,
            adminNames,
            now,
            last7d,
            ct
        );

        return new DashboardMetrics(kpis, executions, topFailures, topDurations, throughputSeries);
    }

    /// <inheritdoc />
    public ServerMetrics GetServerMetrics()
    {
        using var process = Process.GetCurrentProcess();
        var startTimeUtc = process.StartTime.ToUniversalTime();
        var now = DateTime.UtcNow;
        return new ServerMetrics(
            ProcessStartTimeUtc: startTimeUtc,
            UptimeSeconds: (now - startTimeUtc).TotalSeconds,
            WorkingSetBytes: process.WorkingSet64,
            GcHeapBytes: GC.GetTotalMemory(forceFullCollection: false)
        );
    }

    private static async Task<IReadOnlyList<ExecutionsBucket>> BuildExecutionsOverTimeAsync(
        Effect.Data.Services.DataContext.IDataContext db,
        MetricsRange range,
        bool hideAdminTrains,
        DateTime now,
        CancellationToken ct
    )
    {
        var adminNames = AdminTrains.FullNames.ToHashSet();
        var bucketCount = range == MetricsRange.Last60Minutes ? 60 : 24;
        var bucketSize =
            range == MetricsRange.Last60Minutes ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);
        var windowStart = now - TimeSpan.FromTicks(bucketSize.Ticks * bucketCount);

        IQueryable<Effect.Models.Metadata.Metadata> q = db
            .Metadatas.AsNoTracking()
            .Where(m => m.StartTime >= windowStart);
        if (hideAdminTrains)
            q = q.Where(m => !adminNames.Contains(m.Name));

        // Group by raw date-parts in SQL, then materialise the DateTime in memory.
        // Constructing DateTimes inside .Select projections doesn't reliably translate
        // across providers, so we keep it provider-agnostic.
        var raw =
            range == MetricsRange.Last60Minutes
                ? (
                    await q.GroupBy(m => new
                        {
                            m.StartTime.Date,
                            m.StartTime.Hour,
                            m.StartTime.Minute,
                            m.TrainState,
                        })
                        .Select(g => new
                        {
                            g.Key.Date,
                            g.Key.Hour,
                            g.Key.Minute,
                            g.Key.TrainState,
                            Count = g.Count(),
                        })
                        .ToListAsync(ct)
                )
                    .Select(x => new
                    {
                        Bucket = DateTime.SpecifyKind(
                            x.Date.AddHours(x.Hour).AddMinutes(x.Minute),
                            DateTimeKind.Utc
                        ),
                        x.TrainState,
                        x.Count,
                    })
                    .ToList()
                : (
                    await q.GroupBy(m => new
                        {
                            m.StartTime.Date,
                            m.StartTime.Hour,
                            m.TrainState,
                        })
                        .Select(g => new
                        {
                            g.Key.Date,
                            g.Key.Hour,
                            g.Key.TrainState,
                            Count = g.Count(),
                        })
                        .ToListAsync(ct)
                )
                    .Select(x => new
                    {
                        Bucket = DateTime.SpecifyKind(x.Date.AddHours(x.Hour), DateTimeKind.Utc),
                        x.TrainState,
                        x.Count,
                    })
                    .ToList();

        // Truncate "now" to the bucket boundary so labels line up.
        var lastBucket =
            range == MetricsRange.Last60Minutes
                ? DateTime.SpecifyKind(
                    now.Date.AddHours(now.Hour).AddMinutes(now.Minute),
                    DateTimeKind.Utc
                )
                : DateTime.SpecifyKind(now.Date.AddHours(now.Hour), DateTimeKind.Utc);

        return Enumerable
            .Range(0, bucketCount)
            .Select(i =>
            {
                var bucketStart =
                    lastBucket - TimeSpan.FromTicks(bucketSize.Ticks * (bucketCount - 1 - i));
                int Sum(TrainState s) =>
                    raw.Where(x => x.Bucket == bucketStart && x.TrainState == s).Sum(x => x.Count);
                return new ExecutionsBucket(
                    bucketStart,
                    Completed: Sum(TrainState.Completed),
                    Failed: Sum(TrainState.Failed),
                    Cancelled: Sum(TrainState.Cancelled)
                );
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<ThroughputSeries>> BuildThroughputSeriesAsync(
        Effect.Data.Services.DataContext.IDataContext db,
        bool hideAdminTrains,
        HashSet<string> adminNames,
        DateTime now,
        DateTime last7d,
        CancellationToken ct
    )
    {
        IQueryable<Effect.Models.Metadata.Metadata> q = db
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Completed && m.StartTime >= last7d);
        if (hideAdminTrains)
            q = q.Where(m => !adminNames.Contains(m.Name));

        // 6-hour blocks. Keep the bucket calc identical to the dashboard's existing logic
        // (group on raw date-parts, materialise DateTime in memory).
        var stats = (
            await q.GroupBy(m => new
                {
                    m.StartTime.Date,
                    Block = m.StartTime.Hour / 6,
                    m.Name,
                })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Block,
                    g.Key.Name,
                    Count = g.Count(),
                })
                .ToListAsync(ct)
        )
            .Select(x => new
            {
                Bucket = DateTime.SpecifyKind(x.Date.AddHours(x.Block * 6), DateTimeKind.Utc),
                x.Name,
                x.Count,
            })
            .ToList();

        const int blockCount = 28; // 7 days * 4 blocks/day
        var lastBlockStart = DateTime.SpecifyKind(
            now.Date.AddHours((now.Hour / 6) * 6),
            DateTimeKind.Utc
        );
        var bucketStarts = Enumerable
            .Range(0, blockCount)
            .Select(i => lastBlockStart.AddHours(-6 * (blockCount - 1 - i)))
            .ToList();

        var top3 = stats
            .GroupBy(x => x.Name)
            .OrderByDescending(g => g.Sum(x => x.Count))
            .Take(3)
            .Select(g => g.Key)
            .ToList();
        var top3Set = top3.ToHashSet();

        ThroughputSeries SeriesFor(string name, Func<string, bool> match)
        {
            var buckets = bucketStarts
                .Select(b => new ThroughputBucket(
                    b,
                    stats.Where(x => x.Bucket == b && match(x.Name)).Sum(x => x.Count)
                ))
                .ToList();
            return new ThroughputSeries(name, buckets);
        }

        var series = top3.Select(name => SeriesFor(name, n => n == name)).ToList();
        series.Add(SeriesFor("Other", n => !top3Set.Contains(n)));

        // Drop empty series so consumers don't render blank lines.
        return series.Where(s => s.Buckets.Any(b => b.Count > 0)).ToList();
    }

    /// <inheritdoc />
    public SchedulerConfigSnapshot GetSchedulerConfig()
    {
        var cfg = _schedulerConfiguration;
        return new SchedulerConfigSnapshot(
            ManifestManagerEnabled: cfg.ManifestManagerEnabled,
            JobDispatcherEnabled: cfg.JobDispatcherEnabled,
            ManifestManagerPollingInterval: cfg.ManifestManagerPollingInterval,
            JobDispatcherPollingInterval: cfg.JobDispatcherPollingInterval,
            MaxActiveJobs: cfg.MaxActiveJobs,
            DefaultMaxRetries: cfg.DefaultMaxRetries,
            DefaultRetryDelay: cfg.DefaultRetryDelay,
            RetryBackoffMultiplier: cfg.RetryBackoffMultiplier,
            MaxRetryDelay: cfg.MaxRetryDelay,
            DefaultJobTimeout: cfg.DefaultJobTimeout,
            StalePendingTimeout: cfg.StalePendingTimeout,
            RecoverStuckJobsOnStartup: cfg.RecoverStuckJobsOnStartup,
            DeadLetterRetentionPeriod: cfg.DeadLetterRetentionPeriod,
            AutoPurgeDeadLetters: cfg.AutoPurgeDeadLetters,
            LocalWorkerCount: _localWorkerOptions?.WorkerCount,
            MetadataCleanupInterval: cfg.MetadataCleanup?.CleanupInterval,
            MetadataCleanupRetention: cfg.MetadataCleanup?.RetentionPeriod
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdateSchedulerConfigAsync(
        UpdateSchedulerConfigInput input,
        CancellationToken ct
    )
    {
        if (ValidateSchedulerConfigPatch(input) is { } refusal)
            return new OperationResult(
                false,
                Id: SchedulerConfig.SingletonId,
                Message: $"Scheduler config not updated: {refusal}"
            );

        var cfg = _schedulerConfiguration;
        var changed = 0;

        // Apply each patch field to the in-memory singleton. `changed` is incremented
        // only when the value actually differs, so `updated_at` is bumped accurately
        // and a no-op patch returns Count: 0 with no DB write.
        if (input.ManifestManagerEnabled is { } v1 && cfg.ManifestManagerEnabled != v1)
        {
            cfg.ManifestManagerEnabled = v1;
            changed++;
        }
        if (input.JobDispatcherEnabled is { } v2 && cfg.JobDispatcherEnabled != v2)
        {
            cfg.JobDispatcherEnabled = v2;
            changed++;
        }
        if (
            input.ManifestManagerPollingInterval is { } v3
            && cfg.ManifestManagerPollingInterval != v3
        )
        {
            cfg.ManifestManagerPollingInterval = v3;
            changed++;
        }
        if (input.JobDispatcherPollingInterval is { } v4 && cfg.JobDispatcherPollingInterval != v4)
        {
            cfg.JobDispatcherPollingInterval = v4;
            changed++;
        }

        if (input.ClearMaxActiveJobs)
        {
            if (cfg.MaxActiveJobs is not null)
            {
                cfg.MaxActiveJobs = null;
                changed++;
            }
        }
        else if (input.MaxActiveJobs is { } maxJobs && cfg.MaxActiveJobs != maxJobs)
        {
            cfg.MaxActiveJobs = maxJobs;
            changed++;
        }

        if (input.DefaultMaxRetries is { } v5 && cfg.DefaultMaxRetries != v5)
        {
            cfg.DefaultMaxRetries = v5;
            changed++;
        }
        if (input.DefaultRetryDelay is { } v6 && cfg.DefaultRetryDelay != v6)
        {
            cfg.DefaultRetryDelay = v6;
            changed++;
        }
        if (input.RetryBackoffMultiplier is { } v7 && cfg.RetryBackoffMultiplier != v7)
        {
            cfg.RetryBackoffMultiplier = v7;
            changed++;
        }
        if (input.MaxRetryDelay is { } v8 && cfg.MaxRetryDelay != v8)
        {
            cfg.MaxRetryDelay = v8;
            changed++;
        }
        if (input.DefaultJobTimeout is { } v9 && cfg.DefaultJobTimeout != v9)
        {
            cfg.DefaultJobTimeout = v9;
            changed++;
        }
        if (input.StalePendingTimeout is { } v10 && cfg.StalePendingTimeout != v10)
        {
            cfg.StalePendingTimeout = v10;
            changed++;
        }
        if (input.RecoverStuckJobsOnStartup is { } v11 && cfg.RecoverStuckJobsOnStartup != v11)
        {
            cfg.RecoverStuckJobsOnStartup = v11;
            changed++;
        }
        if (input.DeadLetterRetentionPeriod is { } v12 && cfg.DeadLetterRetentionPeriod != v12)
        {
            cfg.DeadLetterRetentionPeriod = v12;
            changed++;
        }
        if (input.AutoPurgeDeadLetters is { } v13 && cfg.AutoPurgeDeadLetters != v13)
        {
            cfg.AutoPurgeDeadLetters = v13;
            changed++;
        }

        if (_localWorkerOptions is not null)
        {
            if (input.ClearLocalWorkerCount)
            {
                // LocalWorkerOptions.WorkerCount is non-nullable; "clear" resets to processor count.
                var def = Environment.ProcessorCount;
                if (_localWorkerOptions.WorkerCount != def)
                {
                    _localWorkerOptions.WorkerCount = def;
                    changed++;
                }
            }
            else if (input.LocalWorkerCount is { } wc && _localWorkerOptions.WorkerCount != wc)
            {
                _localWorkerOptions.WorkerCount = wc;
                changed++;
            }
        }

        if (cfg.MetadataCleanup is not null)
        {
            if (
                input.MetadataCleanupInterval is { } v14
                && cfg.MetadataCleanup.CleanupInterval != v14
            )
            {
                cfg.MetadataCleanup.CleanupInterval = v14;
                changed++;
            }
            if (
                input.MetadataCleanupRetention is { } v15
                && cfg.MetadataCleanup.RetentionPeriod != v15
            )
            {
                cfg.MetadataCleanup.RetentionPeriod = v15;
                changed++;
            }
        }

        // No-op patches skip the DB write entirely so `updated_at` only moves on real changes.
        if (changed > 0)
        {
            await PersistAsync(ct);
            _changeSignal?.Notify(ChangeDomain.SchedulerConfig);
        }

        return new OperationResult(
            true,
            Id: SchedulerConfig.SingletonId,
            Count: changed,
            Message: changed == 0
                ? "Scheduler config: no changes."
                : $"Scheduler config: {changed} field(s) updated."
        );
    }

    /// <summary>
    /// The ranges a group patch must stay in, checked before any field is written so a refused
    /// patch changes nothing. The same ranges the dashboard's form enforces, held here so every
    /// caller of the service gets them: priority is a work queue priority, and a limit of zero
    /// would stop the group dispatching at all (<see cref="UpdateManifestGroupInput.ClearMaxActiveJobs"/>
    /// removes the limit instead).
    /// </summary>
    internal static string? ValidateManifestGroupPatch(UpdateManifestGroupInput input)
    {
        var problems = new List<string>();

        if (
            input.Priority is { } priority
            && priority is < WorkQueue.MinPriority or > WorkQueue.MaxPriority
        )
            problems.Add(
                $"Priority must be between {WorkQueue.MinPriority} and {WorkQueue.MaxPriority}."
            );

        if (!input.ClearMaxActiveJobs && input.MaxActiveJobs is < 1)
            problems.Add("MaxActiveJobs must be at least 1; clear it to remove the limit.");

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>
    /// The ranges a scheduler config patch must stay in (<see cref="SchedulerConfigLimits"/>),
    /// checked before any field is applied so a refused patch changes neither the live settings
    /// nor the persisted row.
    /// </summary>
    internal static string? ValidateSchedulerConfigPatch(UpdateSchedulerConfigInput input)
    {
        var problems = new[]
        {
            SchedulerConfigLimits.TimerInterval(
                input.ManifestManagerPollingInterval,
                nameof(input.ManifestManagerPollingInterval)
            ),
            SchedulerConfigLimits.TimerInterval(
                input.JobDispatcherPollingInterval,
                nameof(input.JobDispatcherPollingInterval)
            ),
            input.ClearMaxActiveJobs
                ? null
                : SchedulerConfigLimits.AtLeastOne(
                    input.MaxActiveJobs,
                    nameof(input.MaxActiveJobs)
                ),
            SchedulerConfigLimits.NotNegative(
                input.DefaultMaxRetries,
                nameof(input.DefaultMaxRetries)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                input.DefaultRetryDelay,
                nameof(input.DefaultRetryDelay)
            ),
            SchedulerConfigLimits.BackoffMultiplier(
                input.RetryBackoffMultiplier,
                nameof(input.RetryBackoffMultiplier)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                input.MaxRetryDelay,
                nameof(input.MaxRetryDelay)
            ),
            SchedulerConfigLimits.PositiveDuration(
                input.DefaultJobTimeout,
                nameof(input.DefaultJobTimeout)
            ),
            SchedulerConfigLimits.PositiveDuration(
                input.StalePendingTimeout,
                nameof(input.StalePendingTimeout)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                input.DeadLetterRetentionPeriod,
                nameof(input.DeadLetterRetentionPeriod)
            ),
            input.ClearLocalWorkerCount
                ? null
                : SchedulerConfigLimits.WorkerCount(
                    input.LocalWorkerCount,
                    nameof(input.LocalWorkerCount)
                ),
            SchedulerConfigLimits.TimerInterval(
                input.MetadataCleanupInterval,
                nameof(input.MetadataCleanupInterval)
            ),
            SchedulerConfigLimits.PositiveDuration(
                input.MetadataCleanupRetention,
                nameof(input.MetadataCleanupRetention)
            ),
        }
            .OfType<string>()
            .ToList();

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var row = await db.SchedulerConfigs.FindAsync(
            new object[] { SchedulerConfig.SingletonId },
            ct
        );
        var cfg = _schedulerConfiguration;

        // Snapshot the current in-memory state into the row (it's already been
        // mutated by the caller). Insert if missing, update otherwise.
        // We use DbSet.Add directly (rather than db.Track) because Track infers
        // Added/Modified from `Id > 0`, which would misclassify the singleton row
        // (Id is fixed at 1) as an update on first persist.
        if (row is null)
        {
            row = new SchedulerConfig { Id = SchedulerConfig.SingletonId };
            CopyInto(row, cfg);
            row.UpdatedAt = DateTime.UtcNow;
            db.SchedulerConfigs.Add(row);
        }
        else
        {
            CopyInto(row, cfg);
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChanges(ct);
    }

    private void CopyInto(SchedulerConfig row, SchedulerConfiguration cfg)
    {
        row.ManifestManagerEnabled = cfg.ManifestManagerEnabled;
        row.JobDispatcherEnabled = cfg.JobDispatcherEnabled;
        row.ManifestManagerPollingInterval = cfg.ManifestManagerPollingInterval;
        row.JobDispatcherPollingInterval = cfg.JobDispatcherPollingInterval;
        row.MaxActiveJobs = cfg.MaxActiveJobs;
        row.DefaultMaxRetries = cfg.DefaultMaxRetries;
        row.DefaultRetryDelay = cfg.DefaultRetryDelay;
        row.RetryBackoffMultiplier = cfg.RetryBackoffMultiplier;
        row.MaxRetryDelay = cfg.MaxRetryDelay;
        row.DefaultJobTimeout = cfg.DefaultJobTimeout;
        row.StalePendingTimeout = cfg.StalePendingTimeout;
        row.RecoverStuckJobsOnStartup = cfg.RecoverStuckJobsOnStartup;
        row.DeadLetterRetentionPeriod = cfg.DeadLetterRetentionPeriod;
        row.AutoPurgeDeadLetters = cfg.AutoPurgeDeadLetters;
        row.LocalWorkerCount = _localWorkerOptions?.WorkerCount;
        row.MetadataCleanupInterval = cfg.MetadataCleanup?.CleanupInterval;
        row.MetadataCleanupRetention = cfg.MetadataCleanup?.RetentionPeriod;
    }
}
