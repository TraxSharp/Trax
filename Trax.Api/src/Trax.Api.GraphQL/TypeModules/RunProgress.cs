using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Types;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>Where a followed run stands.</summary>
internal enum RunProgressState
{
    /// <summary>Queued, and not yet dispatched.</summary>
    Queued,

    /// <summary>Dispatched, and not yet running.</summary>
    Pending,

    /// <summary>Running.</summary>
    InProgress,

    /// <summary>Finished.</summary>
    Completed,

    /// <summary>Ended with a failure.</summary>
    Failed,

    /// <summary>Cancelled.</summary>
    Cancelled,
}

/// <summary>A run as its starter reads it: where it stands and the steps it took, or why it cannot be read.</summary>
/// <param name="RunId">The run's external id.</param>
/// <param name="Train">The train's field, as the schema serves it (<c>startRun</c>).</param>
/// <param name="State">Where the run stands, or null on a refusal.</param>
/// <param name="Steps">The steps the run recorded so far, in order, at most 500.</param>
/// <param name="Failure">Why the run failed, when a junction threw a <c>TrainException</c>; null otherwise.</param>
/// <param name="Problem">Why the run cannot be read, or null.</param>
/// <param name="RetriedAs">The id of the run that retries this failed one, once it is queued; null otherwise.</param>
internal sealed record RunProgressAnswer(
    string RunId,
    string? Train,
    RunProgressState? State,
    IReadOnlyList<JunctionStep> Steps,
    string? Failure,
    SnapshotProblem? Problem,
    string? RetriedAs = null
);

/// <summary>One event of a followed run: a step, a change of state, or the end of the stream.</summary>
/// <param name="RunId">The run's external id.</param>
/// <param name="Sequence">The event's number in this stream; a skipped number means events were lost, so read the run again.</param>
/// <param name="State">The run's new state, on a change of state; else null.</param>
/// <param name="Step">The step, on a step; else null.</param>
/// <param name="Failure">Why the run failed, when a junction threw a <c>TrainException</c>.</param>
/// <param name="Problem">Why the stream was refused, or null. The last event of the stream.</param>
/// <param name="RetriedAs">The id of the run that retries this failed one, when it is queued. The last event of the stream.</param>
internal sealed record RunProgressEvent(
    string RunId,
    long Sequence,
    RunProgressState? State,
    JunctionStep? Step,
    string? Failure,
    SnapshotProblem? Problem,
    string? RetriedAs = null
);

/// <summary>
/// The caller's own runs of a train marked <c>[TraxRunProgress]</c>, followed through
/// <c>subscription { runProgress(runId) }</c> and read through <c>query { runProgress(runId) }</c>. Only the caller who
/// started the run may follow it (its <c>starter_hash</c>); a missing id, another caller's, a run nobody started
/// directly and a run of a train that has not opted in all answer <c>NOT_FOUND</c>, through one query. A step's answers
/// and decision track are withheld unless the train opted in to show them, and a failure carries its message only when
/// a junction threw a <c>TrainException</c>. See Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md.
/// </summary>
internal sealed class RunProgressTypeModule(ITrainDiscoveryService discovery) : TypeModule
{
    /// <summary>The subscription and query field.</summary>
    public const string Field = "runProgress";

    internal const string NotFoundMessage = "There is no run with this id that you may follow.";

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var types = new List<ITypeSystemMember>();
        if (!discovery.DiscoverTrains().Any(r => r.IsRunProgressEnabled))
            return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);

        types.Add(new EnumType<RunProgressState>());
        types.Add(AnswerType());
        types.Add(EventType());
        types.Add(
            new ObjectTypeExtension(d =>
            {
                d.Name("RootQuery");
                d.Field(Field)
                    .Authorize(ApplyPolicy.BeforeResolver)
                    .Description(
                        "Your run of a train that lets its starter follow it: where it stands and the steps it took. "
                            + "A run you did not start answers NOT_FOUND."
                    )
                    .Argument("runId", a => a.Type<NonNullType<StringType>>())
                    .Type(new NonNullTypeNode(new NamedTypeNode("RunProgress")))
                    .Resolve(ctx => ReadAsync(ctx));
            })
        );
        types.Add(
            new ObjectTypeExtension(d =>
            {
                d.Name(MachineChangedSubscription.SubscriptionType);
                d.Field(Field)
                    .Description(
                        "Your run of a train that lets its starter follow it: each step and each change of state as it "
                            + "happens, ending after the run ends. A run you did not start answers NOT_FOUND."
                    )
                    .Argument("runId", a => a.Type<NonNullType<StringType>>())
                    .Type(new NonNullTypeNode(new NamedTypeNode("RunProgressEvent")))
                    .Resolve(ctx => ctx.GetEventMessage<RunProgressEvent>())
                    .Subscribe(ctx => SubscribeAsync(ctx));
            })
        );
        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static ObjectType AnswerType() =>
        new(d =>
        {
            d.Name("RunProgress");
            d.Description("A run as its starter reads it.");
            d.Field("runId")
                .Type<NonNullType<StringType>>()
                .Resolve(c => c.Parent<RunProgressAnswer>().RunId);
            d.Field("train")
                .Type<StringType>()
                .Description(
                    "The train's field, as the schema serves it; null for a train the schema does not expose."
                )
                .Resolve(c => c.Parent<RunProgressAnswer>().Train);
            d.Field("state")
                .Type<EnumType<RunProgressState>>()
                .Resolve(c => c.Parent<RunProgressAnswer>().State);
            d.Field("steps")
                .Type<NonNullType<ListType<NonNullType<JunctionStepGraphType>>>>()
                .Description("The steps recorded so far, in order, at most 500.")
                .Resolve(c => c.Parent<RunProgressAnswer>().Steps);
            d.Field("failure")
                .Type<StringType>()
                .Description("Why the run failed, when a junction threw a TrainException.")
                .Resolve(c => c.Parent<RunProgressAnswer>().Failure);
            d.Field("problem")
                .Type<ObjectType<SnapshotProblem>>()
                .Resolve(c => c.Parent<RunProgressAnswer>().Problem);
            d.Field("retriedAs")
                .Type<StringType>()
                .Description(
                    "The id of the run that retries this failed one, once it is queued: follow that run next."
                )
                .Resolve(c => c.Parent<RunProgressAnswer>().RetriedAs);
        });

    private static ObjectType EventType() =>
        new(d =>
        {
            d.Name("RunProgressEvent");
            d.Description(
                "One event of a followed run: a step, a change of state, or the end of the stream."
            );
            d.Field("runId")
                .Type<NonNullType<StringType>>()
                .Resolve(c => c.Parent<RunProgressEvent>().RunId);
            d.Field("sequence")
                .Type<NonNullType<LongType>>()
                .Description(
                    "This event's number; a skipped number means events were lost, so read the run again."
                )
                .Resolve(c => c.Parent<RunProgressEvent>().Sequence);
            d.Field("state")
                .Type<EnumType<RunProgressState>>()
                .Resolve(c => c.Parent<RunProgressEvent>().State);
            d.Field("step")
                .Type<JunctionStepGraphType>()
                .Resolve(c => c.Parent<RunProgressEvent>().Step);
            d.Field("failure")
                .Type<StringType>()
                .Resolve(c => c.Parent<RunProgressEvent>().Failure);
            d.Field("problem")
                .Type<ObjectType<SnapshotProblem>>()
                .Resolve(c => c.Parent<RunProgressEvent>().Problem);
            d.Field("retriedAs")
                .Type<StringType>()
                .Description(
                    "The id of the run that retries this failed one: follow that run next. The last event of the stream."
                )
                .Resolve(c => c.Parent<RunProgressEvent>().RetriedAs);
        });

    private static async Task<RunProgressAnswer> ReadAsync(IResolverContext ctx)
    {
        var runId = ctx.ArgumentValue<string>("runId");
        var access = ctx.Services.GetRequiredService<RunProgressAccess>();
        var run = await access
            .FindAsync(runId, ctx.Services, ctx.RequestAborted)
            .ConfigureAwait(false);
        if (run is null)
            return new RunProgressAnswer(runId, null, null, [], null, NotFound());

        var steps = new List<JunctionStep>();
        if (run.MetadataId is { } metadataId)
        {
            using var db = await ctx
                .Services.GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(ctx.RequestAborted)
                .ConfigureAwait(false);
            var rows = await db
                .JunctionRuns.AsNoTracking()
                .ForRun(metadataId)
                .Take(500)
                .ToListAsync(ctx.RequestAborted)
                .ConfigureAwait(false);
            foreach (var row in rows)
                if (run.Visibility.Present(Wrap(run, JunctionStep.From(row))) is { } shown)
                    steps.Add(shown.Junction);
        }
        return new RunProgressAnswer(
            runId,
            run.Field,
            run.State,
            steps,
            run.Failure,
            null,
            run.RetriedAs
        );
    }

    private static async ValueTask<ISourceStream> SubscribeAsync(IResolverContext ctx)
    {
        var services = ctx.Services;
        var runId = ctx.ArgumentValue<string>("runId");
        var user = ctx.GetGlobalStateOrDefault<ClaimsPrincipal>("ClaimsPrincipal");
        if (user?.Identity?.IsAuthenticated != true)
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        var access = services.GetRequiredService<RunProgressAccess>();
        var run = await access.FindAsync(runId, services, ctx.RequestAborted).ConfigureAwait(false);
        if (run is null)
            return Once(new RunProgressEvent(runId, 1, null, null, null, NotFound()));

        var lease = services.GetRequiredService<RunProgressLimits>().TryAcquire(run.Starter, runId);
        if (lease is null)
            return Once(
                new RunProgressEvent(
                    runId,
                    1,
                    null,
                    null,
                    null,
                    new SnapshotProblem
                    {
                        Code = RefusalCode.TooManySubscriptions,
                        Message =
                            $"You may follow one run {RunProgressLimits.PerRun} times and {RunProgressLimits.PerPrincipal} "
                            + "runs in all. Complete one to open another.",
                    }
                )
            );

        var receiver = services.GetRequiredService<ITopicEventReceiver>();
        var sender = services.GetRequiredService<ITopicEventSender>();
        ISourceStream<TrainLifecycleEvent>? states = null;
        ISourceStream<JunctionEvent>? steps = null;
        try
        {
            // Subscribed before the state is read, so a change in between is not lost.
            states = await TopicSubscribe
                .OneAtATimeAsync<TrainLifecycleEvent>(
                    receiver,
                    nameof(LifecycleSubscriptions.OnTrainStateChanged),
                    ctx.RequestAborted
                )
                .ConfigureAwait(false);
            steps = await TopicSubscribe
                .OneAtATimeAsync<JunctionEvent>(
                    receiver,
                    nameof(LifecycleSubscriptions.OnJunctionEvent),
                    ctx.RequestAborted
                )
                .ConfigureAwait(false);
            var publisher = LifecycleEventPublisher.For(sender);
            var statesBaseline = await publisher
                .LastPublishedAsync(
                    nameof(LifecycleSubscriptions.OnTrainStateChanged),
                    ctx.RequestAborted
                )
                .ConfigureAwait(false);
            var stepsBaseline = await publisher
                .LastPublishedAsync(
                    nameof(LifecycleSubscriptions.OnJunctionEvent),
                    ctx.RequestAborted
                )
                .ConfigureAwait(false);
            return new RunProgressStream(
                run,
                states,
                statesBaseline,
                steps,
                stepsBaseline,
                token =>
                    publisher.LastPublishedAsync(
                        nameof(LifecycleSubscriptions.OnJunctionEvent),
                        token
                    ),
                lease
            )
            {
                NextTry = access.NextTryAsync,
                ReadQueued = access.QueuedFateAsync,
                Logger = services.GetService<ILoggerFactory>()?.CreateLogger<RunProgressStream>(),
            };
        }
        catch
        {
            if (states is not null)
                await states.DisposeAsync().ConfigureAwait(false);
            if (steps is not null)
                await steps.DisposeAsync().ConfigureAwait(false);
            lease.Dispose();
            throw;
        }
    }

    internal static JunctionEvent Wrap(RunProgressRun run, JunctionStep step) =>
        new(
            run.MetadataId ?? 0,
            run.RunId,
            run.TrainName,
            JunctionEventType.JunctionCompleted,
            DateTime.UtcNow,
            step
        );

    internal static SnapshotProblem NotFound() =>
        new() { Code = RefusalCode.NotFound, Message = NotFoundMessage };

    private static ISourceStream Once(RunProgressEvent answer) => new RunProgressStream(answer);
}

/// <summary>What became of a run first found queued (<see cref="RunProgressAccess.QueuedFateAsync"/>).</summary>
internal enum QueuedFate
{
    /// <summary>Its entry is still queued.</summary>
    Waiting,

    /// <summary>It was dispatched, or ran: its lifecycle events say the rest.</summary>
    Dispatched,

    /// <summary>Its entry was cancelled before dispatch.</summary>
    Cancelled,

    /// <summary>Its entry is gone and no run was recorded under its id.</summary>
    Gone,
}

/// <summary>One run a caller may follow, as <see cref="RunProgressAccess.FindAsync"/> found it.</summary>
internal sealed record RunProgressRun(
    string RunId,
    string TrainName,
    string? Field,
    long? MetadataId,
    RunProgressState State,
    string? Failure,
    string Starter,
    LifecycleVisibility Visibility
)
{
    /// <summary>The manifest the run is one occurrence of, or null.</summary>
    public long? ManifestId { get; init; }

    /// <summary>When the run ended, or null.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>The run that retries this failed one, once it is queued.</summary>
    public string? RetriedAs { get; init; }
}

/// <summary>
/// Finds a run the current caller started, of a train that lets its starter follow it: the latest run under the id, or
/// the queued entry when none has run yet. Everything else is not found, through one query per table, so a missing id
/// and another caller's take the same path.
/// </summary>
internal sealed class RunProgressAccess(
    ITrainDiscoveryService discovery,
    IDataContextProviderFactory contexts
)
{
    private readonly Lazy<
        IReadOnlyDictionary<string, (TrainRegistration Train, string? Field)>
    > _trains = new(() =>
    {
        // Every train that opts in, exposed or not: a scheduled or invoked train has no field of its own, but its
        // starter follows its runs all the same.
        var trains = discovery.DiscoverTrains();
        var fields = TrainTypeModule.AssignTrainNames(trains);
        return trains
            .Where(t => t.IsRunProgressEnabled)
            .ToDictionary(
                t => t.ServiceType.FullName!,
                t =>
                    (
                        t,
                        fields.TryGetValue(t, out var field)
                            ? TrainTypeModule.CamelCase(field)
                            : (string?)null
                    )
            );
    });

    public async Task<RunProgressRun?> FindAsync(
        string runId,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        var starters = Starters(services);
        if (starters.Count == 0 || runId.Length != 32 || _trains.Value.Count == 0)
            return null;
        var names = _trains.Value.Keys.ToList();

        using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ran = await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ExternalId == runId
                && m.StarterHash != null
                && starters.Contains(m.StarterHash)
                && names.Contains(m.Name)
            )
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.TrainState,
                m.FailureException,
                m.FailureReason,
                m.StarterHash,
                m.ManifestId,
                m.EndTime,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (ran is not null)
        {
            var found = Run(
                runId,
                ran.Name,
                ran.Id,
                StateOf(ran.TrainState),
                string.Equals(ran.FailureException, "TrainException", StringComparison.Ordinal)
                    ? ran.FailureReason
                    : null,
                ran.StarterHash!
            ) with
            {
                ManifestId = ran.ManifestId,
                EndedAt = ran.EndTime,
            };
            return found.State == RunProgressState.Failed && found.ManifestId is not null
                ? found with
                {
                    RetriedAs = (await NextTryAsync(found, ct).ConfigureAwait(false)).RetriedAs,
                }
                : found;
        }

        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w =>
                w.ExternalId == runId
                && w.Status == WorkQueueStatus.Queued
                && w.StarterHash != null
                && starters.Contains(w.StarterHash)
                && names.Contains(w.TrainName)
            )
            .Select(w => new { w.TrainName, w.StarterHash })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return queued is null
            ? null
            : Run(
                runId,
                queued.TrainName,
                null,
                RunProgressState.Queued,
                null,
                queued.StarterHash!
            );
    }

    /// <summary>
    /// What became of <paramref name="run"/>, first found queued: its latest entry's status for the same starter, or,
    /// with no entry, whether a run was recorded under its id.
    /// </summary>
    public async Task<QueuedFate> QueuedFateAsync(RunProgressRun run, CancellationToken ct)
    {
        using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        var status = await db
            .WorkQueues.AsNoTracking()
            .Where(w => w.ExternalId == run.RunId && w.StarterHash == run.Starter)
            .OrderByDescending(w => w.Id)
            .Select(w => (WorkQueueStatus?)w.Status)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return status switch
        {
            WorkQueueStatus.Queued => QueuedFate.Waiting,
            WorkQueueStatus.Cancelled => QueuedFate.Cancelled,
            WorkQueueStatus.Dispatched => QueuedFate.Dispatched,
            _ => await db
                .Metadatas.AsNoTracking()
                .AnyAsync(m => m.ExternalId == run.RunId && m.StarterHash == run.Starter, ct)
                .ConfigureAwait(false)
                ? QueuedFate.Dispatched
                : QueuedFate.Gone,
        };
    }

    /// <summary>
    /// For a failed run of a manifest: the run that retries it, once queued, and whether no retry will come because
    /// the manifest was dead-lettered after it or is disabled. A retry is the manifest's next run or queued entry for
    /// the same starter.
    /// </summary>
    public async Task<(string? RetriedAs, bool NoRetry)> NextTryAsync(
        RunProgressRun run,
        CancellationToken ct
    )
    {
        using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        // A run first seen while queued knows neither its metadata nor its manifest yet: its row has them now.
        var ran = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == run.RunId && m.StarterHash == run.Starter)
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.ManifestId,
                m.EndTime,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (ran?.ManifestId is not { } manifestId)
            return (null, ran is not null);
        var metadataId = ran.Id;
        var next = await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ManifestId == manifestId && m.Id > metadataId && m.StarterHash == run.Starter
            )
            .OrderBy(m => m.Id)
            .Select(m => m.ExternalId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (next is not null)
            return (next, false);

        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w =>
                w.ManifestId == manifestId
                && w.Status == WorkQueueStatus.Queued
                && w.StarterHash == run.Starter
            )
            .OrderBy(w => w.Id)
            .Select(w => w.ExternalId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (queued is not null)
            return (queued, false);

        // A disabled manifest is not retried, so the stream need not wait out RetryWait for it.
        var disabled = await db
            .Manifests.AsNoTracking()
            .AnyAsync(m => m.Id == manifestId && !m.IsEnabled, ct)
            .ConfigureAwait(false);
        if (disabled)
            return (null, true);

        var since = ran.EndTime ?? run.EndedAt ?? DateTime.MinValue;
        var deadLettered = await db
            .DeadLetters.AsNoTracking()
            .AnyAsync(d => d.ManifestId == manifestId && d.DeadLetteredAt >= since, ct)
            .ConfigureAwait(false);
        return (null, deadLettered);
    }

    private RunProgressRun Run(
        string runId,
        string trainName,
        long? metadataId,
        RunProgressState state,
        string? failure,
        string starter
    )
    {
        var (train, field) = _trains.Value[trainName];
        var visibility = new LifecycleVisibility(
            false,
            new HashSet<string>(StringComparer.Ordinal) { trainName }
        )
        {
            JunctionAnswers = train.RunProgressAnswers,
        };
        return new RunProgressRun(
            runId,
            trainName,
            field,
            metadataId,
            state,
            failure,
            starter,
            visibility
        );
    }

    // The caller as a run records its starter: the principal id the mediator read when the caller started it, and the
    // user key a machine records when it invokes the train for its owner.
    private static List<string> Starters(IServiceProvider services)
    {
        var principalId = services.GetService<ICurrentPrincipalProvider>()?.GetCurrentPrincipalId();
        var userKey = services.GetService<ISnapshotPrincipal>()?.CurrentUserKey;
        return new[] { RunStarter.OfPrincipal(principalId), RunStarter.OfUserKey(userKey) }
            .Where(h => h is not null)
            .Select(h => h!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal static RunProgressState StateOf(TrainState state) =>
        state switch
        {
            TrainState.Pending => RunProgressState.Pending,
            TrainState.InProgress => RunProgressState.InProgress,
            TrainState.Completed => RunProgressState.Completed,
            TrainState.Failed => RunProgressState.Failed,
            _ => RunProgressState.Cancelled,
        };
}

/// <summary>
/// How many runProgress streams a caller may hold open on this host: <see cref="PerRun"/> on one run and
/// <see cref="PerPrincipal"/> in all. A stream holds its place until it ends.
/// </summary>
internal sealed class RunProgressLimits
{
    /// <summary>The most streams one caller holds on one run.</summary>
    public const int PerRun = 10;

    /// <summary>The most streams one caller holds in all.</summary>
    public const int PerPrincipal = 500;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Starter, string RunId), int> _byRun = [];
    private readonly Dictionary<string, int> _byStarter = new(StringComparer.Ordinal);

    /// <summary>A place for one more stream of <paramref name="starter"/> on <paramref name="runId"/>, or null when full.</summary>
    public IDisposable? TryAcquire(string starter, string runId)
    {
        lock (_gate)
        {
            var onRun = _byRun.GetValueOrDefault((starter, runId));
            var inAll = _byStarter.GetValueOrDefault(starter);
            if (onRun >= PerRun || inAll >= PerPrincipal)
                return null;
            _byRun[(starter, runId)] = onRun + 1;
            _byStarter[starter] = inAll + 1;
        }
        return new Lease(this, starter, runId);
    }

    private void Release(string starter, string runId)
    {
        lock (_gate)
        {
            if (_byRun.TryGetValue((starter, runId), out var onRun))
            {
                if (onRun <= 1)
                    _byRun.Remove((starter, runId));
                else
                    _byRun[(starter, runId)] = onRun - 1;
            }
            if (_byStarter.TryGetValue(starter, out var inAll))
            {
                if (inAll <= 1)
                    _byStarter.Remove(starter);
                else
                    _byStarter[starter] = inAll - 1;
            }
        }
    }

    private sealed class Lease(RunProgressLimits limits, string starter, string runId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                limits.Release(starter, runId);
        }
    }
}

/// <summary>
/// One runProgress stream: the run's changes of state and its steps, read from the two lifecycle topics, kept to the
/// run, passed through its visibility and numbered as one sequence that skips a number after any loss on either topic.
/// It ends after the run ends, and releases both topics and its place once.
/// </summary>
internal sealed class RunProgressStream : ISourceStream<RunProgressEvent>
{
    private readonly RunProgressRun? _run;
    private readonly ISourceStream<TrainLifecycleEvent>? _states;
    private readonly ISourceStream<JunctionEvent>? _steps;
    private readonly long _statesBaseline;
    private readonly long _stepsBaseline;
    private readonly IDisposable? _lease;
    private readonly RunProgressEvent? _only;
    private readonly Func<CancellationToken, ValueTask<long>>? _stepsLastPublished;

    /// <summary>Where a failed run's retry is looked up, after the run ends.</summary>
    public Func<
        RunProgressRun,
        CancellationToken,
        Task<(string? RetriedAs, bool NoRetry)>
    >? NextTry { get; init; }

    /// <summary>What became of a run first found queued, read while it waits.</summary>
    public Func<RunProgressRun, CancellationToken, Task<QueuedFate>>? ReadQueued { get; init; }

    /// <summary>Where a failed read of a queued run is logged.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>How the stream waits between two reads; replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>How long a failed run of a manifest waits for its retry before the stream ends without one.</summary>
    internal static TimeSpan RetryWait { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long the stream waits before it first looks for the retry again. It doubles up to <see cref="RetryPollMax"/>.</summary>
    internal static TimeSpan RetryPoll { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between two looks for the retry.</summary>
    internal static TimeSpan RetryPollMax { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a run first found queued is read while it waits, so one cancelled before dispatch, which publishes no
    /// lifecycle event, still ends its stream.
    /// </summary>
    internal static TimeSpan QueuedPoll { get; set; } = TimeSpan.FromSeconds(5);

    // Cancelled when the stream is disposed, so a read the subscription never cancels (a retry wait, a queued poll)
    // does not outlive the stream's place.
    private readonly CancellationTokenSource _disposing = new();
    private int _disposed;

    public RunProgressStream(RunProgressEvent only) => _only = only;

    public RunProgressStream(
        RunProgressRun run,
        ISourceStream<TrainLifecycleEvent> states,
        long statesBaseline,
        ISourceStream<JunctionEvent> steps,
        long stepsBaseline,
        Func<CancellationToken, ValueTask<long>> stepsLastPublished,
        IDisposable lease
    )
    {
        _stepsLastPublished = stepsLastPublished;
        _run = run;
        _states = states;
        _statesBaseline = statesBaseline;
        _steps = steps;
        _stepsBaseline = stepsBaseline;
        _lease = lease;
    }

    public IAsyncEnumerable<RunProgressEvent> ReadEventsAsync() => Read<RunProgressEvent>();

    IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _disposing.CancelAsync().ConfigureAwait(false);
        if (_states is not null)
            await _states.DisposeAsync().ConfigureAwait(false);
        if (_steps is not null)
            await _steps.DisposeAsync().ConfigureAwait(false);
        _lease?.Dispose();
    }

    private async IAsyncEnumerable<TOut> Read<TOut>(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        if (_only is not null)
        {
            yield return (TOut)(object)_only;
            yield break;
        }

        var run = _run!;
        var merged = Channel.CreateUnbounded<(
            TrainLifecycleEvent? State,
            JunctionEvent? Step,
            bool Lost
        )>();
        var stepsSeen = new StrongBox<long>(_stepsBaseline);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposing.Token);
        var pumps = Task.WhenAll(
            Pump(
                _states!,
                new StrongBox<long>(_statesBaseline),
                e => e.PublishSequence,
                e => (e, null),
                merged.Writer,
                stop.Token
            ),
            Pump(
                _steps!,
                stepsSeen,
                e => e.PublishSequence,
                e => (null, e),
                merged.Writer,
                stop.Token
            )
        );
        _ = pumps.ContinueWith(_ => merged.Writer.TryComplete(), TaskScheduler.Default);
        // A queued run cancelled before dispatch publishes nothing, so its fate is read while it waits.
        var closed =
            run.State == RunProgressState.Queued
                ? WatchQueued(run, merged.Writer, stop.Token)
                : null;

        var sequence = 0L;
        try
        {
            // The state the run was in when it was found comes first, so a stream opened after the run ended still
            // says how it ended, and then the stream ends.
            sequence++;
            yield return (TOut)
                (object)
                    new RunProgressEvent(run.RunId, sequence, run.State, null, run.Failure, null);
            if (IsEnd(run.State))
            {
                if (run.RetriedAs is { } known)
                    yield return (TOut)
                        (object)
                            new RunProgressEvent(
                                run.RunId,
                                ++sequence,
                                null,
                                null,
                                null,
                                null,
                                known
                            );
                else
                    await foreach (var retry in RetryOf(run, stop.Token).ConfigureAwait(false))
                        yield return (TOut)
                            (object)
                                new RunProgressEvent(
                                    run.RunId,
                                    ++sequence,
                                    null,
                                    null,
                                    null,
                                    null,
                                    retry
                                );
                yield break;
            }

            RunProgressEvent? end = null;
            await foreach (var item in merged.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                if (closed is not null && item is { State: null, Step: null, Lost: false })
                {
                    if (await closed.ConfigureAwait(false) is { } last)
                        yield return (TOut)(object)(last with { Sequence = ++sequence });
                    yield break;
                }
                if (Next(run, item, ref sequence, out end) is { } next)
                    yield return (TOut)(object)next;
                if (end is not null)
                    break;
            }
            if (end is null)
                yield break;

            // The run published its steps before its end, but on another topic, which may be read later. The steps
            // published until the end was read are waited for, briefly: a step dropped on the way never arrives, and
            // counts as a loss instead.
            var target = await _stepsLastPublished!(stop.Token).ConfigureAwait(false);
            using (var settle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                settle.CancelAfter(SettleBound);
                while (Volatile.Read(ref stepsSeen.Value) < target)
                {
                    try
                    {
                        await Task.Delay(SettlePoll, settle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                    {
                        sequence++;
                        break;
                    }
                }
            }
            while (merged.Reader.TryRead(out var item))
                if (item.Step is not null && Next(run, item, ref sequence, out _) is { } step)
                    yield return (TOut)(object)step;

            yield return (TOut)(object)(end with { Sequence = ++sequence });

            await foreach (
                var retry in RetryOf(run with { State = end.State!.Value }, stop.Token)
                    .ConfigureAwait(false)
            )
                yield return (TOut)
                    (object)
                        new RunProgressEvent(run.RunId, ++sequence, null, null, null, null, retry);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            if (closed is not null)
                try
                {
                    await closed.ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
                {
                    // The watch only ends this way once the stream is stopping.
                }
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    // A failed run of a manifest is retried by the scheduler after its delay: the id of the retry once it is queued,
    // and nothing when the manifest is dead-lettered, when RetryWait passes, or when the run did not fail.
    private async IAsyncEnumerable<string> RetryOf(
        RunProgressRun run,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        if (run.State != RunProgressState.Failed || NextTry is null)
            yield break;
        var failed = run with { EndedAt = run.EndedAt ?? DateTime.UtcNow.AddSeconds(-5) };
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waiting.CancelAfter(RetryWait);
        // Backed off, so many streams waiting on retries that are minutes away do not poll the database every second.
        var poll = RetryPoll;
        while (true)
        {
            (string? RetriedAs, bool NoRetry) next;
            try
            {
                next = await NextTry(failed, waiting.Token).ConfigureAwait(false);
                if (next.RetriedAs is null && !next.NoRetry)
                {
                    await Delay(poll, waiting.Token).ConfigureAwait(false);
                    poll = poll * 2 < RetryPollMax ? poll * 2 : RetryPollMax;
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            if (next.RetriedAs is { } retry)
            {
                yield return retry;
                yield break;
            }
            if (next.NoRetry)
                yield break;
        }
    }

    // Reads a run first found queued every QueuedPoll until it is dispatched, whose lifecycle events then say the rest,
    // or until it is cancelled or gone, which publish none. Then it writes an empty item to wake the reader and answers
    // the event to end on. A read that fails is tried again after a wait that doubles up to RetryPollMax, and after
    // QueuedFailureBound failures in a row the stream ends with a problem, so the client is told and subscribes again.
    private async Task<RunProgressEvent?> WatchQueued(
        RunProgressRun run,
        ChannelWriter<(TrainLifecycleEvent?, JunctionEvent?, bool)> writer,
        CancellationToken ct
    )
    {
        if (ReadQueued is null)
            return null;
        try
        {
            var wait = QueuedPoll;
            var failures = 0;
            while (true)
            {
                await Delay(wait, ct).ConfigureAwait(false);
                QueuedFate fate;
                try
                {
                    fate = await ReadQueued(run, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    // A dropped connection, a read that timed out and the like: the run is read again later, so a
                    // cancel after it still ends the stream and releases its place.
                    if (++failures >= QueuedFailureBound)
                    {
                        Logger?.LogWarning(
                            e,
                            "Reading queued run {RunId} failed {Failures} times in a row; ending its stream.",
                            run.RunId,
                            failures
                        );
                        await writer.WriteAsync((null, null, false), ct).ConfigureAwait(false);
                        return new RunProgressEvent(run.RunId, 0, null, null, null, Unreadable());
                    }
                    Logger?.LogWarning(
                        e,
                        "Reading queued run {RunId} failed; reading it again.",
                        run.RunId
                    );
                    wait = wait * 2 < RetryPollMax ? wait * 2 : RetryPollMax;
                    continue;
                }
                failures = 0;
                wait = QueuedPoll;
                if (fate == QueuedFate.Dispatched)
                    return null;
                if (fate == QueuedFate.Waiting)
                    continue;
                var last =
                    fate == QueuedFate.Cancelled
                        ? new RunProgressEvent(
                            run.RunId,
                            0,
                            RunProgressState.Cancelled,
                            null,
                            null,
                            null
                        )
                        : new RunProgressEvent(
                            run.RunId,
                            0,
                            null,
                            null,
                            null,
                            RunProgressTypeModule.NotFound()
                        );
                await writer.WriteAsync((null, null, false), ct).ConfigureAwait(false);
                return last;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>How many reads of a queued run may fail in a row before its stream ends with a problem.</summary>
    internal const int QueuedFailureBound = 10;

    internal static SnapshotProblem Unreadable() =>
        new()
        {
            Code = RefusalCode.InternalError,
            Message = "The run could not be read. Subscribe again.",
        };

    /// <summary>How long the end of a run waits for its last steps before reporting them lost.</summary>
    internal static readonly TimeSpan SettleBound = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(5);

    private static bool IsEnd(RunProgressState? state) =>
        state
            is RunProgressState.Completed
                or RunProgressState.Failed
                or RunProgressState.Cancelled;

    // One item of the merged topics as this run's starter sees it: a step to send, nothing, or the run's end, which is
    // held in end (unnumbered) rather than sent, so its last steps can go first.
    private static RunProgressEvent? Next(
        RunProgressRun run,
        (TrainLifecycleEvent? State, JunctionEvent? Step, bool Lost) item,
        ref long sequence,
        out RunProgressEvent? end
    )
    {
        end = null;
        if (item.Lost)
        {
            sequence++;
            return null;
        }
        if (item.Step is { } step)
        {
            if (
                !string.Equals(step.ExternalId, run.RunId, StringComparison.Ordinal)
                || run.Visibility.Present(step) is not { } shownStep
            )
                return null;
            return new RunProgressEvent(
                run.RunId,
                ++sequence,
                null,
                shownStep.Junction,
                null,
                null
            );
        }
        if (
            item.State is not { } state
            || !string.Equals(state.ExternalId, run.RunId, StringComparison.Ordinal)
            || run.Visibility.Present(state) is not { } shown
        )
            return null;
        var now = RunProgressAccess.StateOf(shown.TrainState);
        var failure =
            shown.FailureReason == LifecycleSubscriptionAccess.MaskedFailureReason
                ? null
                : shown.FailureReason;
        var answer = new RunProgressEvent(run.RunId, 0, now, null, failure, null);
        if (IsEnd(now))
        {
            end = answer;
            return null;
        }
        return answer with { Sequence = ++sequence };
    }

    // Reads one topic into the merged channel, marking a jump in its publish numbers as a loss. The highest number
    // read is recorded only after its event is in the channel, so a reader that sees it may read the event.
    private static async Task Pump<T>(
        ISourceStream<T> stream,
        StrongBox<long> seen,
        Func<T, long> publishSequence,
        Func<T, (TrainLifecycleEvent?, JunctionEvent?)> wrap,
        ChannelWriter<(TrainLifecycleEvent?, JunctionEvent?, bool)> writer,
        CancellationToken ct
    )
    {
        var last = seen.Value;
        try
        {
            await foreach (
                var e in stream.ReadEventsAsync().WithCancellation(ct).ConfigureAwait(false)
            )
            {
                var published = publishSequence(e);
                if (published > last + 1)
                    await writer.WriteAsync((null, null, true), ct).ConfigureAwait(false);
                var (state, step) = wrap(e);
                await writer.WriteAsync((state, step, false), ct).ConfigureAwait(false);
                if (published > last)
                {
                    last = published;
                    Volatile.Write(ref seen.Value, last);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
