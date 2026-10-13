using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.TypeModules;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// The operator's live view of state-machine instances, <c>subscription { onMachineInstanceChanged(machine:) }</c>:
/// each instance whenever any path on any host writes it, with its state, revision and whether it waits on a run,
/// never its context or its owner's key. Behind the operations gate, and read through
/// <see cref="IOperationsService.WatchMachineInstancesAsync"/>, the feed the dashboard's State machines page reads, so
/// both views follow the host's one change listener. Registered with the operations queries. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal static class MachineInstanceFeed
{
    /// <summary>The subscription field.</summary>
    public const string Field = "onMachineInstanceChanged";

    private const string PrincipalState = "ClaimsPrincipal";

    /// <summary>The field, added to Trax's subscription root.</summary>
    public static ObjectTypeExtension Extension() =>
        new(d =>
        {
            d.Name(MachineChangedSubscription.SubscriptionType);
            d.Field(Field)
                .Description(
                    "Every state-machine instance as it is written, or every instance of one machine: its state, "
                        + "revision and whether it waits on a run it invoked, and once more with deleted set when it "
                        + "is deleted or expires. Never its context or its owner. Behind the operations gate."
                )
                .Argument(
                    "machine",
                    a => a.Type<StringType>().Description("Only this machine's instances.")
                )
                .Type<NonNullType<ObjectType<MachineInstanceChange>>>()
                .Resolve(ctx => ctx.GetEventMessage<MachineInstanceChange>())
                .Subscribe(SubscribeAsync);
        });

    private static async ValueTask<ISourceStream> SubscribeAsync(IResolverContext ctx)
    {
        var access = ctx.Services.GetRequiredService<LifecycleSubscriptionAccess>();
        var user = ctx.GetGlobalStateOrDefault<ClaimsPrincipal>(PrincipalState);
        if (!await access.OperationsFor(user).ConfigureAwait(false))
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        var operations = ctx.Services.GetRequiredService<IOperationsService>();
        var stop = new CancellationTokenSource();
        var feed = operations.WatchMachineInstancesAsync(
            ctx.ArgumentValue<string?>("machine"),
            stop.Token
        );
        return new FeedStream(feed, stop);
    }

    /// <summary>
    /// The feed as one subscriber reads it. It starts reading the feed when the subscription is made, not when
    /// HotChocolate first reads the stream, so nothing written in between is lost; what it has read waits in a bounded
    /// buffer, and a subscriber that falls that far behind is ended rather than left stale. Disposing it ends the feed.
    /// </summary>
    private sealed class FeedStream : ISourceStream<MachineInstanceChange>
    {
        /// <summary>How many events wait for a slow subscriber before its stream is ended.</summary>
        private const int Capacity = 4096;

        private readonly CancellationTokenSource _stop;
        private readonly Channel<MachineInstanceChange> _buffer =
            Channel.CreateBounded<MachineInstanceChange>(
                new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = true }
            );
        private int _disposed;

        public FeedStream(
            IAsyncEnumerable<MachineInstanceRecord> feed,
            CancellationTokenSource stop
        )
        {
            _stop = stop;
            _ = PumpAsync(feed, stop.Token);
        }

        public IAsyncEnumerable<MachineInstanceChange> ReadEventsAsync() =>
            Read<MachineInstanceChange>();

        IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

        public ValueTask DisposeAsync()
        {
            // Cancelled, never disposed: the feed's enumeration may still be reading its token.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _stop.Cancel();
            return ValueTask.CompletedTask;
        }

        private async Task PumpAsync(
            IAsyncEnumerable<MachineInstanceRecord> feed,
            CancellationToken ct
        )
        {
            try
            {
                await foreach (var record in feed.WithCancellation(ct).ConfigureAwait(false))
                    if (!_buffer.Writer.TryWrite(MachineInstanceChange.From(record)))
                        break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Disposed.
            }
            catch (Exception ex)
            {
                _buffer.Writer.TryComplete(ex);
                return;
            }
            finally
            {
                _stop.Cancel();
            }
            _buffer.Writer.TryComplete();
        }

        private async IAsyncEnumerable<TOut> Read<TOut>(
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            try
            {
                await foreach (var change in _buffer.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    yield return (TOut)(object)change;
            }
            finally
            {
                await DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
