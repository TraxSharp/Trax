using System.Data.Common;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
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
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
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

        return await EnqueueAsync(
            registration,
            input.InputJson,
            input.Priority,
            input.ScheduledAt,
            replayDecisionsOf: null,
            ct
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> RequeueExecutionAsync(long metadataId, CancellationToken ct)
    {
        string trainName;
        string savedInput;
        bool hasDecisionsToReplay;

        using (var db = await _dataContextFactory.CreateDbContextAsync(ct))
        {
            var source = await db
                .Metadatas.AsNoTracking()
                .Where(m => m.Id == metadataId)
                .Select(m => new { m.Name, m.Input })
                .FirstOrDefaultAsync(ct);

            if (source is null)
                return new OperationResult(false, Message: $"Execution {metadataId} not found.");

            // Re-queueing reads the saved input back as the train's input. Nothing saved, a
            // placeholder saved in its place, or masked [TraxSensitive] members would all read
            // back as defaults, and the train would run with values it never had.
            var refusal = RequeueInputCheck.RefusalFor(metadataId, source.Input);
            if (refusal is not null || source.Input is not { } input)
                return new OperationResult(false, Message: refusal);

            trainName = source.Name;
            savedInput = input;

            // Only a run with decisions to replay is linked: one that recorded a decision, or one
            // that was itself queued to replay another's. The second is a requeue of a requeue
            // that recorded nothing, or only some of its questions, before it ended; the replay
            // follows the link back to the answers it did not record. A run of a train that never
            // decides, or on a host that does not record decisions, is re-queued exactly as an
            // ordinary enqueue, through the overload every ITrainExecutionService has.
            hasDecisionsToReplay = await db.HasDecisionsToReplay(metadataId, ct);
        }

        // The same lookup as QueueTrainAsync. The caller named a run, not a train, so the miss is
        // reported as the run's train having gone rather than as an unknown name to look up.
        var registration = _discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r => r.ServiceType.FullName == trainName);

        if (registration is null)
            return new OperationResult(
                false,
                Message: RequeueInputCheck.TrainNoLongerRegistered(metadataId, trainName)
            );

        // The saved input carries the reference metadata SaveTrainParameters() writes ($id,
        // $values, $ref), which the mediator does not honour in an input it is handed: a list
        // would be refused and a repeated object read back at its defaults. Resolving it first
        // hands over the plain tree the run was given. It does not depend on the input type, so
        // doing it before the mediator authorizes the caller tells them nothing about the input.
        string requeuedInput;

        try
        {
            requeuedInput = TrainInputReader.ResolveSavedInput(
                savedInput,
                registration,
                MaxInputJsonBytes()
            );
        }
        catch (JsonException ex)
        {
            return new OperationResult(
                false,
                Message: $"Execution {metadataId}'s saved input cannot be read back as the input "
                    + $"it ran with: {ex.Message}"
            );
        }
        catch (TrainInputValidationException ex)
        {
            // Generic by design, as in EnqueueAsync.
            return new OperationResult(false, Message: ex.Message);
        }

        // The replay link is set here and nowhere a caller can reach, and only to the run being
        // re-queued, so the new run replays decisions of a run of the same train (docs/0041).
        return await EnqueueAsync(
            registration,
            requeuedInput,
            priority: 0,
            scheduledAt: null,
            replayDecisionsOf: hasDecisionsToReplay ? metadataId : null,
            ct,
            requeueOf: metadataId
        );
    }

    /// <summary>
    /// The enqueue <see cref="QueueTrainAsync"/> and <see cref="RequeueExecutionAsync"/> share,
    /// for a train already found by name: through the mediator, with refusals and failures split
    /// as scheduler/0004 says.
    /// </summary>
    private async Task<OperationResult> EnqueueAsync(
        TrainRegistration registration,
        string? inputJson,
        int priority,
        DateTime? scheduledAt,
        long? replayDecisionsOf,
        CancellationToken ct,
        long? requeueOf = null
    )
    {
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
            // Only a replay needs the options overload; every other enqueue goes through the
            // overload every implementation has.
            queued = replayDecisionsOf is null
                ? await _trainExecution.QueueAsync(
                    registration.ServiceType.FullName!,
                    inputJson,
                    priority,
                    scheduledAt,
                    ct
                )
                : await _trainExecution.QueueAsync(
                    registration.ServiceType.FullName!,
                    inputJson,
                    new QueueTrainOptions
                    {
                        Priority = priority,
                        ScheduledAt = scheduledAt,
                        ReplayDecisionsOf = replayDecisionsOf,
                    },
                    ct
                );
        }
        catch (JsonException ex)
        {
            // A re-queue's caller supplied no JSON: the input is the run's own, saved when the
            // train took another shape, so the refusal names the run rather than an InputJson.
            return new OperationResult(
                false,
                Message: requeueOf is { } run
                    ? RequeueInputCheck.SavedInputNoLongerReads(run, registration, ex)
                    : $"Invalid InputJson: {ex.Message}"
            );
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
        catch (TrainAuthorizationNotConfiguredException ex)
        {
            // The train declares [TraxAuthorize] and the host registered no enforcer. The host is
            // misconfigured; that is not an answer about this enqueue, so it is logged and thrown
            // like an infrastructure failure, never reported as a refusal. See scheduler/0004.
            _logger?.LogError(
                ex,
                "Queueing {TrainName} failed: the host has no ITrainAuthorizationService",
                registration.ServiceType.FullName
            );
            throw;
        }
        catch (DecisionReplayNotSupportedException ex)
        {
            // A re-queue that replays decisions reached an ITrainExecutionService (a custom one,
            // or a decorator) that does not implement the overload carrying the link. The host is
            // misconfigured, as with a missing enforcer, so it is logged and thrown rather than
            // reported as a refusal. See scheduler/0004.
            _logger?.LogError(
                ex,
                "Re-queueing {TrainName} failed: {Implementation} cannot queue a run that replays "
                    + "decisions",
                registration.ServiceType.FullName,
                ex.ImplementationType.FullName
            );
            throw;
        }
        catch (Exception ex)
            when (ex is not UnauthorizedAccessException and not OperationCanceledException)
        {
            // A refusal: the train's OnQueue hook or QueueSubjectKey threw, the subject key could
            // not be used, or a deferred entry was cancelled before it was confirmed.
            return Refused("The enqueue was refused", ex, registration.ServiceType.FullName!);
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

        // The mediator's step: authorization before the input is read, so a caller who may not run
        // the train learns nothing about its input from a parse error, then the input read exactly
        // as a queue reads it. An UnauthorizedAccessException, and the missing-enforcer
        // TrainAuthorizationNotConfiguredException, are not caught: neither is an answer about
        // this run.
        object runInput;
        TrainRegistration prepared;

        try
        {
            var preparation = await _trainExecution.PrepareAsync(trainName, input.InputJson, ct);
            prepared = preparation.Registration;
            runInput = preparation.Input;
            CheckStoredInputSize(services, prepared, runInput);
        }
        catch (TrainNotFoundException)
        {
            return new OperationResult(
                false,
                Message: $"Unknown train: {input.TrainName}. Use operations.getTrains to list registered trains."
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

        // A subject-keyed train serializes its work through the queue (docs/0019), which a run
        // started now bypasses, so only a trusted caller may bypass it (docs/0037).
        if (
            prepared.HasQueueSubjectKey
            && services.GetService<ITrustedExecutionScope>() is not { IsTrusted: true }
        )
            return new OperationResult(
                false,
                Message: $"{trainName} declares QueueSubjectKey, so its work for one subject runs "
                    + "one at a time through the work queue, and it is run now only inside a "
                    + "trusted scope. Queue it instead."
            );

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

            // The per-record checks a queue applies (docs/0037): the train's OnQueue hook runs
            // on this input, under the run's ExternalId, before the row is saved, and its writes
            // on the enqueue context are saved with it. A refusal writes nothing.
            var refusal = await RunQueueHookAsync(services, prepared, runInput, metadata, db, ct);
            if (refusal is not null)
                return refusal;

            await db.SaveChanges(ct);
        }

        // The row exists now, so the run is the server's to see through: the submit is not tied
        // to the caller's request. A client that navigates away would otherwise abort the submit,
        // and a runner that takes the request's cancellation would cancel the run with it. The
        // submitter's own timeouts bound the call.
        try
        {
            await ResolveSubmitter(services, trainName)
                .EnqueueAsync(metadata.Id, runInput, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A submitter that throws may still have delivered the job: a runner that ran it and
            // answered with an error, or is still running it when the call times out, has moved
            // the row out of Pending and owns its outcome. Only a row still Pending is a job no
            // runner started; it is failed now with the submitter's exception, as the job
            // dispatcher does when a dispatch fails, and the failure is the server's, not a
            // refusal, so it is thrown (scheduler/0004).
            var failed = await FailUnsubmittedRunAsync(metadata.Id, ex);

            if (failed == 0)
            {
                _logger?.LogWarning(
                    ex,
                    "The submitter reported a failure for a run of {TrainName} (metadata "
                        + "{MetadataId}), but a runner has already started it; the run records "
                        + "its own outcome",
                    trainName,
                    metadata.Id
                );

                return new OperationResult(
                    true,
                    Id: metadata.Id,
                    Count: 1,
                    Message: $"Run {metadata.Id} of {trainName} submitted; its outcome is pending "
                        + "on the run."
                );
            }

            _logger?.LogError(
                ex,
                "Submitting a run of {TrainName} (metadata {MetadataId}) failed",
                trainName,
                metadata.Id
            );
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
    /// Runs the train's <c>OnQueue</c> hook for a run and answers as <see cref="QueueTrainAsync"/>
    /// answers for the same exception: null when the hook accepted, a refusal result when it
    /// refused, and a thrown exception for an infrastructure failure, an authorization failure or
    /// a cancellation (scheduler/0004).
    /// </summary>
    private async Task<OperationResult?> RunQueueHookAsync(
        IServiceProvider services,
        TrainRegistration registration,
        object runInput,
        Metadata metadata,
        IDataContext db,
        CancellationToken ct
    )
    {
        if (!RunQueueHook.Declared(registration))
            return null;

        try
        {
            await RunQueueHook.InvokeAsync(
                services,
                registration,
                runInput,
                metadata.ExternalId,
                db,
                ct
            );
            return null;
        }
        catch (Exception ex)
            when (ex is not UnauthorizedAccessException and not OperationCanceledException
                && IsInfrastructureFailure(ex)
            )
        {
            _logger?.LogError(
                ex,
                "Running {TrainName} failed on infrastructure in its OnQueue hook, not on a refusal",
                metadata.Name
            );
            throw;
        }
        catch (Exception ex)
            when (ex is not UnauthorizedAccessException and not OperationCanceledException)
        {
            return Refused("The run was refused", ex, metadata.Name);
        }
    }

    /// <summary>
    /// Refuses an input whose stored form, the JSON a submitter writes for the worker, is larger
    /// than <see cref="TrainInputReader.StoredInputGrowthFactor"/> times
    /// <c>MaxInputJsonBytes</c>: the cap the mediator holds a queued input's stored form to. The
    /// caller's JSON was capped when it was read; the stored form writes every member and is
    /// indented, so it is measured too, before anything is written or submitted.
    /// </summary>
    /// <exception cref="TrainInputValidationException">The stored form is over its cap.</exception>
    private static void CheckStoredInputSize(
        IServiceProvider services,
        TrainRegistration registration,
        object runInput
    )
    {
        var maxBytes = MaxInputJsonBytes(services);
        var storedCap = (int)
            Math.Min((long)maxBytes * TrainInputReader.StoredInputGrowthFactor, int.MaxValue);

        var stored = JsonSerializer.Serialize(
            runInput,
            registration.InputType,
            TraxJsonSerializationOptions.ManifestProperties
        );
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(stored);

        if (byteCount > storedCap)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                storedCap
            );
    }

    /// <summary>
    /// The mediator's input size cap, or its default when the host registered no
    /// <see cref="MediatorConfiguration"/> or this service was built without a provider.
    /// </summary>
    private int MaxInputJsonBytes() => MaxInputJsonBytes(_services);

    private static int MaxInputJsonBytes(IServiceProvider? services) =>
        services?.GetService<MediatorConfiguration>()?.MaxInputJsonBytes
        ?? new MediatorConfiguration().MaxInputJsonBytes;

    /// <summary>
    /// The submitter the job dispatcher would use for this train: its builder or
    /// <c>[TraxRemote]</c> route when it has one, otherwise the default <see cref="IJobSubmitter"/>.
    /// </summary>
    private static IJobSubmitter ResolveSubmitter(IServiceProvider services, string trainName) =>
        services
            .GetService<JobSubmitterRoutingConfiguration>()
            ?.ResolveSubmitter(services, trainName)
        ?? services.GetRequiredService<IJobSubmitter>();

    /// <summary>
    /// Fails a run whose job was never submitted. Bookkeeping for a run that already failed, so
    /// it is written on <see cref="CancellationToken.None"/>: a cancelled caller is one of the
    /// ways to get here. A failure to write it is logged and dropped, so the caller still sees
    /// why the submit failed; the stale-pending reaper fails the row later.
    /// </summary>
    /// <remarks>
    /// A submitter that throws may still have delivered the job: a remote runner that ran it and
    /// answered with an error, or that is still running it when the HTTP call times out, owns the
    /// row and moves it out of <c>Pending</c>. So the row is failed by a write that matches it
    /// only while it is still <c>Pending</c>. Reading it first and saving afterwards could land
    /// after the runner's claim and record <c>Failed</c> over a run that is in progress.
    /// </remarks>
    /// <returns>
    /// The rows failed: 1 when the run was still <c>Pending</c>, 0 when a runner already owns it,
    /// and <see langword="null"/> when the write itself failed.
    /// </returns>
    private async Task<int?> FailUnsubmittedRunAsync(long metadataId, Exception submitFailure)
    {
        try
        {
            using var db = await _dataContextFactory.CreateDbContextAsync(CancellationToken.None);
            var pending = db.Metadatas.Where(m =>
                m.Id == metadataId && m.TrainState == TrainState.Pending
            );

            var failure = Metadata.Create(
                new CreateMetadata
                {
                    Name = nameof(OperationsService),
                    ExternalId = string.Empty,
                    Input = null,
                }
            );
            failure.AddException(submitFailure);
            var endTime = DateTime.UtcNow;

            if (db.SupportsSetUpdates())
                return await pending.ExecuteUpdateAsync(
                    u =>
                        u.SetProperty(m => m.TrainState, TrainState.Failed)
                            .SetProperty(m => m.EndTime, endTime)
                            .SetProperty(m => m.FailureException, failure.FailureException)
                            .SetProperty(m => m.FailureReason, failure.FailureReason)
                            .SetProperty(m => m.FailureJunction, failure.FailureJunction)
                            .SetProperty(m => m.StackTrace, failure.StackTrace)
                            .SetProperty(m => m.FailureClass, failure.FailureClass),
                    CancellationToken.None
                );

            return await db.UpdateEachAsync(
                pending,
                m =>
                {
                    m.TrainState = TrainState.Failed;
                    m.EndTime = endTime;
                    m.AddException(submitFailure);
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Could not mark unsubmitted run {MetadataId} failed; the stale-pending reaper will",
                metadataId
            );
            return null;
        }
    }

    /// <summary>
    /// The most ids one batch operation takes. A batch is what an operator selects on a page, so
    /// a list longer than this is a caller's mistake, and refusing it keeps one call from
    /// becoming an unbounded statement.
    /// </summary>
    public const int MaxBatchSize = 1000;

    /// <summary>
    /// The largest page a paged read returns. Larger requests are clamped to it, so one call
    /// never materialises a whole table.
    /// </summary>
    public const int MaxPageSize = 500;

    /// <inheritdoc />
    /// <remarks>Served index-only by <c>ix_metadata_manifest_state</c>.</remarks>
    public async Task<ManifestExecutionStats> GetManifestExecutionStatsAsync(
        long manifestId,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
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

    /// <inheritdoc />
    /// <remarks>
    /// The metadata side is served by <c>ix_metadata_manifest_state</c>, the manifest side by
    /// <c>ix_manifest_manifest_group_id</c>.
    /// </remarks>
    public async Task<
        IReadOnlyList<ManifestGroupExecutionStats>
    > GetManifestGroupExecutionStatsAsync(IReadOnlyCollection<long> groupIds, CancellationToken ct)
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<ManifestGroupExecutionStats>();

        if (ids.Length > MaxBatchSize)
            throw new ArgumentOutOfRangeException(
                nameof(groupIds),
                ids.Length,
                $"At most {MaxBatchSize} group ids can be given at once."
            );

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        var manifestCounts = await db
            .Manifests.AsNoTracking()
            .Where(m => ids.Contains(m.ManifestGroupId))
            .GroupBy(m => m.ManifestGroupId)
            .Select(g => new { GroupId = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        // Join runs to the group's manifests, then aggregate per (group, state). The manifest
        // side is filtered to the requested groups first, so each manifest_id seek stays cheap.
        var execAgg = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId != null)
            .Join(
                db.Manifests.AsNoTracking().Where(mf => ids.Contains(mf.ManifestGroupId)),
                m => m.ManifestId,
                mf => (long?)mf.Id,
                (m, mf) =>
                    new
                    {
                        mf.ManifestGroupId,
                        m.TrainState,
                        m.StartTime,
                    }
            )
            .GroupBy(x => new { x.ManifestGroupId, x.TrainState })
            .Select(g => new
            {
                g.Key.ManifestGroupId,
                g.Key.TrainState,
                Count = (long)g.Count(),
                LastRun = g.Max(x => (DateTime?)x.StartTime),
            })
            .ToListAsync(ct);

        return ids.Select(id =>
            {
                var manifestCount = manifestCounts.FirstOrDefault(x => x.GroupId == id)?.Count ?? 0;
                var rows = execAgg.Where(x => x.ManifestGroupId == id).ToList();
                long StateCount(TrainState state) =>
                    rows.Where(x => x.TrainState == state).Sum(x => x.Count);
                return new ManifestGroupExecutionStats(
                    id,
                    ManifestCount: manifestCount,
                    TotalExecutions: rows.Sum(x => x.Count),
                    Completed: StateCount(TrainState.Completed),
                    Failed: StateCount(TrainState.Failed),
                    InProgress: StateCount(TrainState.InProgress),
                    LastRun: rows.Count == 0 ? null : rows.Max(x => x.LastRun)
                );
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<LogPage> GetLogsAsync(LogQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, MaxPageSize);
        var skip = query.AfterId.HasValue ? 0 : Math.Max(query.Skip, 0);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        var filtered = FilterLogs(db.Logs.AsNoTracking(), query).OrderByDescending(l => l.Id);
        IQueryable<Trax.Effect.Models.Log.Log> page = query.AfterId is { } afterId
            ? filtered.Where(l => l.Id < afterId)
            : filtered.Skip(skip);

        var items = await page.Take(take)
            .Select(l => new LogRecord(
                l.Id,
                l.MetadataId,
                l.EventId,
                l.Level,
                l.Category,
                l.Message,
                l.Exception,
                l.StackTrace
            ))
            .ToListAsync(ct);

        return new LogPage(items, skip, take, items.Count > 0 ? items[^1].Id : null);
    }

    /// <inheritdoc />
    public async Task<int> CountLogsAsync(LogQuery query, CancellationToken ct)
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        return await FilterLogs(db.Logs.AsNoTracking(), query).CountAsync(ct);
    }

    private static IQueryable<Trax.Effect.Models.Log.Log> FilterLogs(
        IQueryable<Trax.Effect.Models.Log.Log> logs,
        LogQuery query
    )
    {
        if (query.MetadataId is { } metadataId)
            logs = logs.Where(l => l.MetadataId == metadataId);

        if (query.MinimumLevel is { } minimumLevel)
            logs = logs.Where(l => l.Level >= minimumLevel);

        if (!string.IsNullOrWhiteSpace(query.Category))
            logs = logs.Where(l => l.Category == query.Category);

        return logs;
    }

    /// <inheritdoc />
    public async Task<OperationResult> CancelExecutionsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken ct
    )
    {
        if (RefuseBatch(ids) is { } refused)
            return refused;

        var distinct = ids.Distinct().ToList();

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var flagged = await ExecutionCancellation.RequestAsync(
            db,
            db.Metadatas.Where(m => distinct.Contains(m.Id)),
            _services?.GetService<ICancellationRegistry>(),
            _changeSignal,
            ct
        );

        return new OperationResult(
            true,
            Count: flagged,
            Message: $"Cancellation requested for {flagged} of {distinct.Count} execution(s)."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> CancelWorkQueueEntriesAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken ct
    )
    {
        if (RefuseBatch(ids) is { } refused)
            return refused;

        var distinct = ids.Distinct().ToList();

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        // One statement with the status test in it, so an entry the dispatcher claims meanwhile
        // keeps its Dispatched status instead of being overwritten.
        var queued = db.WorkQueues.Where(q =>
            distinct.Contains(q.Id) && q.Status == WorkQueueStatus.Queued
        );
        var cancelled = db.SupportsSetUpdates()
            ? await queued.ExecuteUpdateAsync(
                s => s.SetProperty(q => q.Status, WorkQueueStatus.Cancelled),
                ct
            )
            : await db.UpdateEachAsync(queued, q => q.Status = WorkQueueStatus.Cancelled, ct);

        if (cancelled > 0)
            _changeSignal?.Notify(ChangeDomain.WorkQueue);

        return new OperationResult(
            true,
            Count: cancelled,
            Message: $"{cancelled} of {distinct.Count} work queue entry(s) cancelled."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetManifestsEnabledAsync(
        IReadOnlyCollection<long> ids,
        bool enabled,
        CancellationToken ct
    )
    {
        if (RefuseBatch(ids) is { } refused)
            return refused;

        var distinct = ids.Distinct().ToList();

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var differing = db.Manifests.Where(m => distinct.Contains(m.Id) && m.IsEnabled != enabled);
        var changed = db.SupportsSetUpdates()
            ? await differing.ExecuteUpdateAsync(s => s.SetProperty(m => m.IsEnabled, enabled), ct)
            : await db.UpdateEachAsync(differing, m => m.IsEnabled = enabled, ct);

        if (changed > 0)
            _changeSignal?.Notify(ChangeDomain.Manifest);

        return new OperationResult(
            true,
            Count: changed,
            Message: $"{changed} of {distinct.Count} manifest(s) {(enabled ? "enabled" : "disabled")}."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetManifestGroupsEnabledAsync(
        IReadOnlyCollection<long> ids,
        bool enabled,
        CancellationToken ct
    )
    {
        if (RefuseBatch(ids) is { } refused)
            return refused;

        var distinct = ids.Distinct().ToList();

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var changed = await SetGroupsEnabledAsync(
            db,
            db.ManifestGroups.Where(g => distinct.Contains(g.Id)),
            enabled,
            ct
        );

        return new OperationResult(
            true,
            Count: changed,
            Message: $"{changed} of {distinct.Count} manifest group(s) {(enabled ? "enabled" : "disabled")}."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetAllManifestGroupsEnabledAsync(
        bool enabled,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var changed = await SetGroupsEnabledAsync(db, db.ManifestGroups, enabled, ct);

        return new OperationResult(
            true,
            Count: changed,
            Message: $"{changed} manifest group(s) {(enabled ? "enabled" : "disabled")}."
        );
    }

    private async Task<int> SetGroupsEnabledAsync(
        IDataContext db,
        IQueryable<Trax.Effect.Models.ManifestGroup.ManifestGroup> groups,
        bool enabled,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var differing = groups.Where(g => g.IsEnabled != enabled);
        var changed = db.SupportsSetUpdates()
            ? await differing.ExecuteUpdateAsync(
                s => s.SetProperty(g => g.IsEnabled, enabled).SetProperty(g => g.UpdatedAt, now),
                ct
            )
            : await db.UpdateEachAsync(
                differing,
                g =>
                {
                    g.IsEnabled = enabled;
                    g.UpdatedAt = now;
                },
                ct
            );

        if (changed > 0)
            _changeSignal?.Notify(ChangeDomain.ManifestGroup);

        return changed;
    }

    /// <summary>
    /// The failed result for a batch that cannot be run as given: no ids, or more than
    /// <see cref="MaxBatchSize"/>. Null when the list is usable.
    /// </summary>
    private static OperationResult? RefuseBatch(IReadOnlyCollection<long> ids) =>
        BatchRefusal(ids) is { } message
            ? new OperationResult(false, Count: 0, Message: message)
            : null;

    /// <summary>
    /// Why a batch of ids cannot be run as given (none, or more than <see cref="MaxBatchSize"/>),
    /// or null when it can. Shared with the scheduler's dead-letter batch actions, so every batch
    /// an operator can send is bounded the same way.
    /// </summary>
    internal static string? BatchRefusal(IReadOnlyCollection<long>? ids)
    {
        if (ids is null || ids.Count == 0)
            return "No ids were given.";

        if (ids.Count > MaxBatchSize)
            return $"At most {MaxBatchSize} ids can be given at once; {ids.Count} were.";

        return null;
    }

    /// <summary>
    /// The failed result for a refusal. Only a message written for the caller is shown: a plain
    /// <see cref="TrainException"/>'s, which the train author wrote (the rule central
    /// <c>docs/0028</c> applies to a remote run), and the mediator's own refusals, a deferred entry
    /// cancelled before it was confirmed and a hook that ran past <c>MaxQueueHookDuration</c>.
    /// Any other exception is still a refusal, so it is a failed result, but with a fixed message;
    /// its own is logged for an operator. See scheduler/0004.
    /// </summary>
    private OperationResult Refused(string refusal, Exception ex, string trainName)
    {
        if (
            ex.GetType() == typeof(TrainException)
            || ex is QueuedWorkCancelledException or QueueHookTimeoutException
        )
            return new OperationResult(false, Message: $"{refusal}: {ex.Message}");

        _logger?.LogWarning(
            ex,
            "{TrainName} was refused with an exception whose message is not shown to the caller",
            trainName
        );

        return new OperationResult(false, Message: $"{refusal}.");
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
        )
        {
            FailureCountWindow = cfg.FailureCountWindow,
        };
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

        var target = new SchedulerSettingsTarget(_schedulerConfiguration, _localWorkerOptions);

        // The settings the patch sets. One this host does not have (local workers, metadata
        // cleanup) is left alone, as it always was.
        var patch = SchedulerSettings
            .All.Where(s => s.AppliesTo(target))
            .Select(s =>
                s.TryReadPatch(input, out var value) ? (Setting: s, Value: value) : default
            )
            .Where(p => p.Setting is not null)
            .ToList();

        if (patch.Count == 0)
            return new OperationResult(
                true,
                Id: SchedulerConfig.SingletonId,
                Count: 0,
                Message: "Scheduler config: no changes."
            );

        int changes;
        try
        {
            changes = await SaveSchedulerConfigPatchAsync(patch, target, ct);
        }
        catch (DbUpdateException ex)
            when (_services?.GetService<ISqlDialect>() is { } dialect
                && dialect.IsUniqueViolation(ex)
            )
        {
            // Another host made the first save between this one's read and its insert. Its row
            // now exists, so read it and apply the patch to it, as any later save does.
            changes = await SaveSchedulerConfigPatchAsync(patch, target, ct);
        }

        // This host applies the patch at once; every scheduler host, this one included, also
        // picks the saved row up on its next settings refresh.
        foreach (var (setting, value) in patch)
            if (!Equals(setting.ReadLive(target), value))
                setting.WriteLive(target, value);

        // No-op patches skip the DB write entirely so `updated_at` only moves on real changes.
        if (changes > 0)
            _changeSignal?.Notify(ChangeDomain.SchedulerConfig);

        return new OperationResult(
            true,
            Id: SchedulerConfig.SingletonId,
            Count: changes,
            Message: changes == 0
                ? "Scheduler config: no changes."
                : $"Scheduler config: {changes} field(s) updated."
        );
    }

    /// <summary>
    /// Reads the stored row, names in it each setting of <paramref name="patch"/> that changes
    /// what the scheduler runs with, and saves it, creating the row on the first save. Returns
    /// how many settings changed; none writes nothing.
    /// </summary>
    private async Task<int> SaveSchedulerConfigPatchAsync(
        IReadOnlyList<(ISchedulerSetting Setting, object? Value)> patch,
        SchedulerSettingsTarget target,
        CancellationToken ct
    )
    {
        // Read the stored row and name only the settings the patch sets in its overrides; every
        // other setting keeps the value each scheduler host configures in code. A save used to
        // write every field of the saving host's configuration, so a save from an API-only host
        // (whose configuration is the empty one AddTraxJobRunner registers) replaced the
        // scheduler's settings with defaults.
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var row = await db.SchedulerConfigs.FindAsync(
            new object[] { SchedulerConfig.SingletonId },
            ct
        );

        var changes = patch
            .Where(p => IsSchedulerConfigChange(row, p.Setting, p.Value, target))
            .ToList();

        if (changes.Count == 0)
            return 0;

        if (row is null)
        {
            // We use DbSet.Add directly (rather than db.Track) because Track infers
            // Added/Modified from `Id > 0`, which would misclassify the singleton row
            // (Id is fixed at 1) as an update on first persist.
            row = new SchedulerConfig { Id = SchedulerConfig.SingletonId, Overrides = "{}" };

            // Only the overrides decide what applies. The columns are also kept for a host
            // still on a version that reads them, and a scheduler host fills them with the
            // values it runs with, as a save always did.
            if (_schedulerConfiguration.IsSchedulerHost)
                foreach (var setting in SchedulerSettings.All.Where(s => s.AppliesTo(target)))
                {
                    setting.WriteRow(row, setting.ReadLive(target));
                    row.RemoveOverride(setting.Name);
                }

            db.SchedulerConfigs.Add(row);
        }
        else
            SchedulerSettings.UpgradeLegacyRow(row);

        foreach (var (setting, value) in changes)
            setting.WriteRow(row, value);
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChanges(ct);

        return changes.Count;
    }

    /// <summary>
    /// Whether saving <paramref name="value"/> changes what the scheduler runs with. A setting the
    /// row names changes when the value differs from the stored one. A setting it does not name
    /// runs with each scheduler's code value: a scheduler host knows it, and the save changes
    /// something only when the value differs from it. Any other host does not know it, so naming
    /// the setting is always a change there.
    /// </summary>
    private bool IsSchedulerConfigChange(
        SchedulerConfig? row,
        ISchedulerSetting setting,
        object? value,
        SchedulerSettingsTarget target
    )
    {
        if (row is not null && setting.TryReadRow(row, out var stored))
            return !Equals(stored, value);

        return !_schedulerConfiguration.IsSchedulerHost || !Equals(setting.ReadLive(target), value);
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
            SchedulerConfigLimits.PositiveDuration(
                input.FailureCountWindow,
                nameof(input.FailureCountWindow)
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
}
