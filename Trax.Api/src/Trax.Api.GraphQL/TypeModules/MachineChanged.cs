using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// One <c>machineChanged</c> event: the instance's whole snapshot with its revision, or the end of the stream
/// (<see cref="Gone"/>, or a <see cref="Problem"/>).
/// </summary>
/// <param name="RowId">The row the stream follows (the instance's incarnation), or null on a refusal.</param>
/// <param name="Revision">The snapshot's revision, or null when the event carries no snapshot.</param>
/// <param name="State">The state the instance is in, or null when the event carries no snapshot.</param>
/// <param name="Context">The instance's context, or null when the event carries no snapshot.</param>
/// <param name="Gone">Whether the instance was deleted or expired. The last event of the stream.</param>
/// <param name="Problem">Why the stream was refused or ended, or null.</param>
internal sealed record MachineChange(
    long? RowId,
    long? Revision,
    string? State,
    JsonElement? Context,
    bool Gone,
    SnapshotProblem? Problem
)
{
    /// <summary>When the timer of the state the instance is in fires, or null.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// Whether the client must read the instance again because this event cannot stand for it. Every event this
    /// server sends is a whole snapshot, so it never sets it; a client honours it all the same.
    /// </summary>
    public bool Reload { get; init; }

    /// <summary>The terminal answer that carries only a problem, <paramref name="code"/> and <paramref name="message"/>.</summary>
    public static MachineChange Refused(long? rowId, string code, string message) =>
        new(rowId, null, null, null, false, new SnapshotProblem { Code = code, Message = message });
}

/// <summary>
/// The <c>machineChanged</c> subscription: one exposed machine's instance, pushed to its owner whenever any path on
/// any host writes it. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Verified once, then routed by row.</b> At subscribe the user key is read from the socket's
/// <see cref="ISnapshotPrincipal"/>, never an argument, and the machine's watch rule is checked against it
/// (<see cref="ISnapshotWatchAccess"/>). A missing id, another user's, a system instance's and one the rule refuses
/// all get one answer, <see cref="DraftRefusalCodes.NotFound"/>. The stream then follows the row's <c>row_id</c>, the
/// incarnation it verified: nothing about the caller is read again, and a row re-created under the same id never
/// reaches it.</para>
/// <para><b>Order.</b> The watch registers before the row is read, and the first event is the row as that read
/// found it, so a write in between is never lost. Each later event is a newer revision, whole; a burst may arrive
/// as its latest revision only, which loses nothing.</para>
/// <para><b>The end.</b> A delete or an expiry ends the stream with <c>gone</c>, from the database and never from
/// the notice. A row written by a newer version of the machine than this node runs ends it with
/// <see cref="DraftRefusalCodes.ClientOutdated"/>; a row this node cannot read otherwise ends it with the
/// rehydration code. Ending one stream ends no other.</para>
/// <para><b>Limits.</b> A principal holds at most <see cref="MachineWatchLimits.PerInstance"/> streams on one
/// instance and <see cref="MachineWatchLimits.PerPrincipal"/> in all; past either the answer is
/// <see cref="DraftRefusalCodes.TooManySubscriptions"/>, and a stream that ends frees its place. Each stream also
/// holds one of its socket's operations, and ends with the socket's credential.</para>
/// </remarks>
internal static class MachineChangedSubscription
{
    /// <summary>The subscription field.</summary>
    public const string Field = "machineChanged";

    /// <summary>The type each event is.</summary>
    public const string TypeName = "MachineChange";

    /// <summary>The subscription root Trax registers.</summary>
    public const string SubscriptionType = "LifecycleSubscriptions";

    internal const string NotFoundMessage = "There is no instance with this id that you may watch.";

    internal const string OutdatedMessage =
        "A newer version of the machine than this server runs wrote the instance. Reload.";

    /// <summary>The event type and the subscription field over <paramref name="machines"/>.</summary>
    public static IEnumerable<ITypeSystemMember> Types(IReadOnlyList<IMachine> machines)
    {
        var exposed = machines.ToDictionary(m => m.Name, StringComparer.Ordinal);
        yield return EventType();
        yield return new ObjectTypeExtension(d =>
        {
            d.Name(SubscriptionType);
            d.Field(Field)
                .Description(
                    "The caller's instance of an exposed machine, pushed whenever it is written: first as it is now, "
                        + "then each newer revision, whole. The stream ends with gone when the instance is deleted or "
                        + "expires, and with a problem when it cannot go on (client-outdated after a newer version of "
                        + "the machine wrote it)."
                )
                .Argument("machine", a => a.Type<NonNullType<StringType>>())
                .Argument("id", a => a.Type<NonNullType<UuidType>>())
                .Type(new NonNullTypeNode(new NamedTypeNode(TypeName)))
                .Resolve(ctx => ctx.GetEventMessage<MachineChange>())
                .Subscribe(ctx => SubscribeAsync(ctx, exposed));
        });
    }

    private static ObjectType EventType() =>
        new(d =>
        {
            d.Name(TypeName);
            d.Description(
                "One machineChanged event: the instance's whole snapshot with its revision, or the end of the stream."
            );
            d.Field("rowId")
                .Type<LongType>()
                .Description("The incarnation of the instance the stream follows.")
                .Resolve(ctx => ctx.Parent<MachineChange>().RowId);
            d.Field("revision")
                .Type<LongType>()
                .Description(
                    "The snapshot's revision. Drop an event at or below the revision you hold: each is whole, so a "
                        + "skipped revision loses nothing."
                )
                .Resolve(ctx => ctx.Parent<MachineChange>().Revision);
            d.Field("state")
                .Type<StringType>()
                .Description(
                    "The state the instance is in, or null on an event that carries no snapshot."
                )
                .Resolve(ctx => ctx.Parent<MachineChange>().State);
            d.Field("context")
                .Type<AnyType>()
                .Description("The instance's context, as the machine's typed get answers it.")
                .Resolve(ctx => ctx.Parent<MachineChange>().Context);
            d.Field("dueAt")
                .Type<DateTimeType>()
                .Description("When the timer of the state the instance is in fires, or null.")
                .Resolve(ctx => ctx.Parent<MachineChange>().DueAt);
            d.Field("gone")
                .Type<NonNullType<BooleanType>>()
                .Description("The instance was deleted or expired. The last event of the stream.")
                .Resolve(ctx => ctx.Parent<MachineChange>().Gone);
            d.Field("reload")
                .Type<NonNullType<BooleanType>>()
                .Description(
                    "Read the instance again: this event does not stand for it. A skipped revision never sets it."
                )
                .Resolve(ctx => ctx.Parent<MachineChange>().Reload);
            d.Field("problem")
                .Type<ObjectType<SnapshotProblem>>()
                .Description(
                    "Why the stream was refused or ended, or null. The last event of the stream."
                )
                .Resolve(ctx => ctx.Parent<MachineChange>().Problem);
        });

    private static async ValueTask<ISourceStream> SubscribeAsync(
        IResolverContext ctx,
        IReadOnlyDictionary<string, IMachine> exposed
    )
    {
        var services = ctx.Services;
        var name = ctx.ArgumentValue<string>("machine");
        var id = ctx.ArgumentValue<Guid>("id");

        // The topic key is the socket's principal: an anonymous caller has no instance to watch.
        var userKey = services.GetService<ISnapshotPrincipal>()?.CurrentUserKey;
        if (string.IsNullOrWhiteSpace(userKey))
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        long? rowId = null;
        if (
            exposed.TryGetValue(name, out var machine)
            && services.GetService<ISnapshotMachineRegistry>()?.Service(name)
                is ISnapshotWatchAccess access
        )
        {
            var caller = services.GetService<ICallerAuthorization>();
            Actor actor = caller is null
                ? new Actor.Client(userKey)
                : new Actor.Client(userKey, caller.IsInRole, caller.SatisfiesPolicy);
            rowId = await access
                .AuthorizeWatch(actor, id, ctx.RequestAborted)
                .ConfigureAwait(false);
        }
        if (rowId is not { } row || machine is not IMachineInternals internals)
            return Once(MachineChange.Refused(null, DraftRefusalCodes.NotFound, NotFoundMessage));

        var lease = services.GetRequiredService<MachineWatchLimits>().TryAcquire(userKey, row);
        if (lease is null)
            return Once(
                MachineChange.Refused(
                    null,
                    DraftRefusalCodes.TooManySubscriptions,
                    $"You may hold {MachineWatchLimits.PerInstance} subscriptions to one instance and "
                        + $"{MachineWatchLimits.PerPrincipal} in all. Complete one to open another."
                )
            );

        SnapshotWatch watch;
        try
        {
            watch = services.GetRequiredService<SnapshotWatchHub>().Watch(row);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        var order = services.GetService<IMachineIrOrder>();
        return new MachineChangedStream(
            watch,
            lease,
            change => Present(change, machine, internals, order)
        );
    }

    // What the subscriber is sent for one change of its row, and whether it ends the stream.
    internal static (MachineChange Event, bool Ends)? Present(
        SnapshotRowChange change,
        IMachine machine,
        IMachineInternals internals,
        IMachineIrOrder? order
    )
    {
        switch (change.Kind)
        {
            case SnapshotRowChangeKind.Gone:
                return (new MachineChange(change.RowId, null, null, null, true, null), true);
            case SnapshotRowChangeKind.Unreadable:
                return (
                    Newer(change, machine, internals, order)
                        ? Outdated(change)
                        : MachineChange.Refused(
                            change.RowId,
                            change.UnreadableReason ?? RehydrationErrorCodes.Malformed,
                            "The stored instance cannot be read by this version of the machine."
                        ),
                    true
                );
            case SnapshotRowChangeKind.Changed:
                break;
            default:
                return null;
        }

        if (Newer(change, machine, internals, order))
            return (Outdated(change), true);

        // Read as a load reads it: migrated from an older version and validated, so the subscriber is sent what the
        // typed get would answer.
        var stored = new JsonObject
        {
            ["machine"] = change.Machine,
            ["version"] = change.Version,
            ["state"] = change.State,
            ["context"] = JsonNode.Parse(change.Context ?? "{}"),
        };
        return internals.Rehydrate(stored.ToJsonString()) switch
        {
            RehydrationResult.Ok ok => (
                new MachineChange(
                    change.RowId,
                    change.Revision,
                    ok.Snapshot.State,
                    JsonSerializer.SerializeToElement(ok.Snapshot.Context),
                    false,
                    null
                )
                {
                    DueAt = change.DueAt,
                },
                false
            ),
            RehydrationResult.Error { Code: RehydrationErrorCodes.VersionMismatch }
                when change.Version > internals.Version => (Outdated(change), true),
            RehydrationResult.Error error => (
                MachineChange.Refused(
                    change.RowId,
                    error.Code,
                    "The stored instance cannot be read by this version of the machine."
                ),
                true
            ),
            _ => (
                MachineChange.Refused(
                    change.RowId,
                    RehydrationErrorCodes.Malformed,
                    "The stored instance cannot be read by this version of the machine."
                ),
                true
            ),
        };
    }

    private static MachineChange Outdated(SnapshotRowChange change) =>
        MachineChange.Refused(change.RowId, DraftRefusalCodes.ClientOutdated, OutdatedMessage);

    // Whether a newer version of the machine than this node's wrote the row: a higher definition version, or the same
    // version under another IR hash that the stored documents order after this node's. The stored documents answer a
    // hash they do not hold as newer. With no order at all (the host refuses to expose a machine without persisted
    // operations, so only a host that skipped startup gets here) a difference says nothing about which is newer, and
    // an older node's write must not end every stream, so the stream is kept.
    private static bool Newer(
        SnapshotRowChange change,
        IMachine machine,
        IMachineInternals internals,
        IMachineIrOrder? order
    )
    {
        if (change.Version > internals.Version)
            return true;
        if (change.Version < internals.Version)
            return false;
        if (
            change.IrHash is not { } written
            || machine.SchemaHash is not { } own
            || string.Equals(written, own, StringComparison.Ordinal)
        )
            return false;
        return order?.IsNewer(machine.Name, written) ?? false;
    }

    private static ISourceStream Once(MachineChange answer) =>
        new MachineChangedStream(null, null, _ => null, answer);
}

/// <summary>
/// Orders the IR hashes of one machine by when each version was deployed, so a <c>machineChanged</c> stream can tell
/// a row written by a newer version (which ends it) from one written by an older node mid-deploy (which does not).
/// Registered by the persisted operations, which number each version's documents as it registers them.
/// </summary>
internal interface IMachineIrOrder
{
    /// <summary>
    /// Whether <paramref name="irHash"/> is a newer version of <paramref name="machine"/> than this node runs, or null
    /// when this node cannot tell.
    /// </summary>
    bool? IsNewer(string machine, string irHash);
}

/// <summary>
/// How many <c>machineChanged</c> streams a principal may hold open: <see cref="PerInstance"/> on one instance and
/// <see cref="PerPrincipal"/> in all, on this host. A stream holds its place until it ends.
/// </summary>
internal sealed class MachineWatchLimits
{
    /// <summary>The most streams one principal holds on one instance.</summary>
    public const int PerInstance = 10;

    /// <summary>The most streams one principal holds in all.</summary>
    public const int PerPrincipal = 2_000;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string User, long RowId), int> _byInstance = [];
    private readonly Dictionary<string, int> _byPrincipal = new(StringComparer.Ordinal);

    /// <summary>The streams open now.</summary>
    public int Open
    {
        get
        {
            lock (_gate)
                return _byPrincipal.Values.Sum();
        }
    }

    /// <summary>A place for one more stream of <paramref name="user"/> on <paramref name="rowId"/>, or null when full.</summary>
    public IDisposable? TryAcquire(string user, long rowId)
    {
        lock (_gate)
        {
            var onInstance = _byInstance.GetValueOrDefault((user, rowId));
            var inAll = _byPrincipal.GetValueOrDefault(user);
            if (onInstance >= PerInstance || inAll >= PerPrincipal)
                return null;
            _byInstance[(user, rowId)] = onInstance + 1;
            _byPrincipal[user] = inAll + 1;
        }
        return new Lease(this, user, rowId);
    }

    private void Release(string user, long rowId)
    {
        lock (_gate)
        {
            if (_byInstance.TryGetValue((user, rowId), out var onInstance))
            {
                if (onInstance <= 1)
                    _byInstance.Remove((user, rowId));
                else
                    _byInstance[(user, rowId)] = onInstance - 1;
            }
            if (_byPrincipal.TryGetValue(user, out var inAll))
            {
                if (inAll <= 1)
                    _byPrincipal.Remove(user);
                else
                    _byPrincipal[user] = inAll - 1;
            }
        }
    }

    private sealed class Lease(MachineWatchLimits limits, string user, long rowId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                limits.Release(user, rowId);
        }
    }
}

/// <summary>
/// One <c>machineChanged</c> stream: the events of one row watch, and its place under the limits, both released once
/// when the stream ends or HotChocolate disposes it (the client completed it, or the socket went away).
/// </summary>
internal sealed class MachineChangedStream(
    SnapshotWatch? watch,
    IDisposable? lease,
    Func<SnapshotRowChange, (MachineChange Event, bool Ends)?> present,
    MachineChange? only = null
) : ISourceStream<MachineChange>
{
    private int _disposed;

    public IAsyncEnumerable<MachineChange> ReadEventsAsync() => Read<MachineChange>();

    IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        watch?.Dispose();
        lease?.Dispose();
    }

    private async IAsyncEnumerable<TOut> Read<TOut>(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        try
        {
            if (only is not null)
            {
                yield return (TOut)(object)only;
                yield break;
            }
            if (watch is null)
                yield break;

            await foreach (
                var change in watch
                    .Changes.ReadAllAsync(ct)
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
            {
                if (present(change) is not { } next)
                    continue;
                yield return (TOut)(object)next.Event;
                if (next.Ends)
                    yield break;
            }
        }
        finally
        {
            Release();
        }
    }
}
