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
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

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
            return new RunProgressAnswer(
                runId,
                Train: null,
                State: null,
                [],
                Failure: null,
                NotFound()
            );

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
            Problem: null,
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
        var starters = RunProgressAccess.Starters(services);
        var run = await access.FindAsync(runId, starters, ctx.RequestAborted).ConfigureAwait(false);
        if (run is null)
            return Once(
                new RunProgressEvent(runId, 1, State: null, Step: null, Failure: null, NotFound())
            );

        var lease = services.GetRequiredService<RunProgressLimits>().TryAcquire(run.Starter, runId);
        if (lease is null)
            return Once(
                new RunProgressEvent(
                    runId,
                    1,
                    State: null,
                    Step: null,
                    Failure: null,
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
                // The endpoint policy and the run being the caller's are asked again at every
                // connection re-check; a stream they no longer admit ends with the answer a
                // refused subscribe gets.
                Recheck = SubscriptionRecheck.For(
                    services,
                    user,
                    async ct =>
                        await access.FindAsync(runId, starters, ct).ConfigureAwait(false)
                            is not null
                ),
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
