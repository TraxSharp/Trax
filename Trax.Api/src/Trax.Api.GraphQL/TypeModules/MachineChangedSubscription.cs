using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Each exposed machine's push, <c>subscription { &lt;machine&gt;Changed(id) }</c> (<c>topicMapChanged</c>): one
/// instance, pushed to its owner whenever any path on any host writes it, in the machine's own types, the ones its
/// <c>get</c> answers. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Verified once, then routed by row.</b> At subscribe the user key is read from the socket's
/// <see cref="ISnapshotPrincipal"/>, never an argument, and the machine's watch rule is checked against it
/// (<see cref="ISnapshotWatchAccess"/>). A missing id, another user's, a system instance's and one the rule refuses
/// all get one answer, <see cref="RefusalCode.NotFound"/>. The stream then follows the row's <c>row_id</c>, the
/// incarnation it verified, and a row re-created under the same id never reaches it. The watch rule is asked again
/// for the same caller at every connection re-check interval; when it no longer admits the caller to that row the
/// stream ends with <see cref="RefusalCode.NotFound"/>, the answer a refused subscribe gets.</para>
/// <para><b>Order.</b> The watch registers before the row is read, and the first event is the row as that read
/// found it, so a write in between is never lost. Each later event is a newer revision, whole; a burst may arrive
/// as its latest revision only, which loses nothing.</para>
/// <para><b>The end.</b> A delete or an expiry ends the stream with <c>gone</c>, from the database and never from
/// the notice. A row written by a newer version of the machine than this node runs ends it with
/// <see cref="RefusalCode.ClientOutdated"/>; a row this node cannot read otherwise ends it with the
/// rehydration code. Ending one stream ends no other.</para>
/// <para><b>Limits.</b> A principal holds at most <see cref="MachineWatchLimits.PerInstance"/> streams on one
/// instance and <see cref="MachineWatchLimits.PerPrincipal"/> in all; past either the answer is
/// <see cref="RefusalCode.TooManySubscriptions"/>, and a stream that ends frees its place. Each stream also
/// holds one of its socket's operations, and ends with the socket's credential.</para>
/// </remarks>
internal static class MachineChangedSubscription
{
    /// <summary>The subscription root Trax registers.</summary>
    public const string SubscriptionType = "LifecycleSubscriptions";

    internal const string NotFoundMessage = "There is no instance with this id that you may watch.";

    internal const string OutdatedMessage =
        "A newer version of the machine than this server runs wrote the instance. Reload.";

    /// <summary>The push's field for the machine <paramref name="machineId"/>: <c>topic-map</c> is <c>topicMapChanged</c>.</summary>
    public static string FieldOf(string machineId) =>
        MachineSurface.FieldName(machineId) + "Changed";

    /// <summary>The type of one event of a machine's push.</summary>
    public static string TypeNameOf(string pascal) => $"{pascal}MachineChange";

    /// <summary>One push field and its event type per machine in <paramref name="machines"/>.</summary>
    public static IEnumerable<ITypeSystemMember> Types(IReadOnlyList<IMachine> machines)
    {
        foreach (var machine in machines)
        {
            var pascal = MachineSurface.PascalName(machine.Name);
            var hasContext = MachineContextFields.Of(machine, machine.States).Count > 0;
            yield return EventType(machine, pascal, hasContext);
            yield return new MachinePushExtension(
                machine.Name,
                d =>
                {
                    d.Name(SubscriptionType);
                    d.Field(FieldOf(machine.Name))
                        .Description(
                            $"The caller's instance of '{machine.Name}', pushed whenever it is written: first as it is "
                                + "now, then each newer revision, whole. The stream ends with gone when the instance is "
                                + "deleted or expires, and with a problem when it cannot go on (CLIENT_OUTDATED after a "
                                + "newer version of the machine wrote it)."
                        )
                        .Argument("id", a => a.Type<NonNullType<UuidType>>())
                        .Type(new NonNullTypeNode(new NamedTypeNode(TypeNameOf(pascal))))
                        .Resolve(ctx => ctx.GetEventMessage<MachineChange>())
                        .Subscribe(ctx => SubscribeAsync(ctx, machine));
                }
            );
        }
    }

    private static ObjectType EventType(IMachine machine, string pascal, bool hasContext) =>
        new(d =>
        {
            d.Name(TypeNameOf(pascal));
            d.Description(
                $"One event of the push of '{machine.Name}': the instance's whole snapshot with its revision, in the "
                    + "types its get answers, or the end of the stream."
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
                .Type(new NamedTypeNode(MachineSurface.StateTypeName(pascal)))
                .Description(
                    "The state the instance is in, or null on an event that carries no snapshot."
                )
                .Resolve(ctx => ctx.Parent<MachineChange>().State);
            if (hasContext)
                d.Field("context")
                    .Type(new NamedTypeNode(MachineSurface.ContextTypeName(pascal)))
                    .Description(
                        "The instance's context, as get answers it, or null on an event that carries no snapshot."
                    )
                    .Resolve(ctx => ctx.Parent<MachineChange>().Context);
            d.Field("dueAt")
                .Type<DateTimeType>()
                .Description("When the timer of the state the instance is in fires, or null.")
                .Resolve(ctx => ctx.Parent<MachineChange>().DueAt);
            d.Field("dueAtKnown")
                .Type<NonNullType<BooleanType>>()
                .Description(
                    "Whether dueAt can be trusted. A push is the row as it is now, so it always can; the field is "
                        + "here so a push and a get decode with one type."
                )
                .Resolve(_ => true);
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
        IMachine machine
    )
    {
        var services = ctx.Services;
        var id = ctx.ArgumentValue<Guid>("id");

        // The topic key is the socket's principal: an anonymous caller has no instance to watch.
        var userKey = services.GetService<ISnapshotPrincipal>()?.CurrentUserKey;
        if (string.IsNullOrWhiteSpace(userKey))
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        if (
            services.GetService<ISnapshotMachineRegistry>()?.Service(machine.Name)
                is not ISnapshotWatchAccess access
            || machine is not IMachineInternals internals
        )
            return Once(MachineChange.Refused(rowId: null, RefusalCode.NotFound, NotFoundMessage));

        if (
            await access
                .AuthorizeWatch(Caller(services, userKey), id, ctx.RequestAborted)
                .ConfigureAwait(false)
            is not { } row
        )
            return Once(MachineChange.Refused(rowId: null, RefusalCode.NotFound, NotFoundMessage));

        var recheck = RecheckWatch(
            services,
            ctx.GetGlobalStateOrDefault<ClaimsPrincipal>("ClaimsPrincipal"),
            machine,
            userKey,
            id,
            row
        );

        return OpenStream(services, machine, internals, userKey, row, recheck);
    }

    private static SubscriptionRecheck RecheckWatch(
        IServiceProvider services,
        ClaimsPrincipal? principal,
        IMachine machine,
        string userKey,
        Guid id,
        long row
    )
    {
        // The endpoint policy and the watch rule are asked again at every connection re-check, for
        // the same caller and the same row, each in a scope of its own so it shares no data context
        // with the socket's other operations. A stream they no longer admit ends with the answer a
        // refused subscribe gets.
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var recheck = SubscriptionRecheck.For(
            services,
            principal,
            async ct =>
            {
                var scope = scopes.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var scoped = scope.ServiceProvider;
                    return scoped.GetService<ISnapshotMachineRegistry>()?.Service(machine.Name)
                            is ISnapshotWatchAccess current
                        && await current
                            .AuthorizeWatch(Caller(scoped, userKey), id, ct)
                            .ConfigureAwait(false) == row;
                }
            }
        );
        return recheck;
    }

    // Takes a watch lease for the caller and opens the stream of the row's changes, or refuses
    // the subscribe when the caller holds too many.
    private static ISourceStream OpenStream(
        IServiceProvider services,
        IMachine machine,
        IMachineInternals internals,
        string userKey,
        long row,
        SubscriptionRecheck recheck
    )
    {
        var lease = services.GetRequiredService<MachineWatchLimits>().TryAcquire(userKey, row);
        if (lease is null)
            return Once(
                MachineChange.Refused(
                    rowId: null,
                    RefusalCode.TooManySubscriptions,
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
        )
        {
            Recheck = recheck,
            RecheckRefused = MachineChange.Refused(row, RefusalCode.NotFound, NotFoundMessage),
        };
    }

    // The socket's principal as a machine rule reads it: its user key, and its roles and policies
    // where the host can answer them.
    private static Actor Caller(IServiceProvider services, string userKey) =>
        services.GetService<ICallerAuthorization>() is { } caller
            ? new Actor.Client(userKey, caller.IsInRole, caller.SatisfiesPolicy)
            : new Actor.Client(userKey);

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
                return (
                    new MachineChange(
                        change.RowId,
                        Revision: null,
                        State: null,
                        Context: null,
                        Gone: true,
                        Problem: null
                    ),
                    true
                );
            case SnapshotRowChangeKind.Unreadable:
                return (
                    Newer(change, machine, internals, order)
                        ? Outdated(change)
                        : MachineChange.Refused(
                            change.RowId,
                            change.UnreadableReason ?? RefusalCode.Malformed,
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

        return Rehydrated(change, internals);
    }

    // A changed row read as a load reads it, and whether it ends the stream.
    private static (MachineChange Event, bool Ends) Rehydrated(
        SnapshotRowChange change,
        IMachineInternals internals
    )
    {
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
                    (JsonObject)ok.Snapshot.Context.DeepClone(),
                    Gone: false,
                    Problem: null
                )
                {
                    DueAt = change.DueAt,
                },
                false
            ),
            RehydrationResult.Error { Code: RefusalCode.VersionMismatch }
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
                    RefusalCode.Malformed,
                    "The stored instance cannot be read by this version of the machine."
                ),
                true
            ),
        };
    }

    private static MachineChange Outdated(SnapshotRowChange change) =>
        MachineChange.Refused(change.RowId, RefusalCode.ClientOutdated, OutdatedMessage);

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
        new MachineChangedStream(watch: null, lease: null, _ => null, answer);
}
