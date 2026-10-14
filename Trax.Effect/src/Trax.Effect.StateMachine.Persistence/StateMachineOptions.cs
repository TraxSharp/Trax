using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Host-level options for the state-machine subsystem, configured through the
/// <see cref="StateMachinesBuilderExtensions.AddStateMachines(Trax.Effect.Configuration.TraxBuilder.TraxBuilderWithEffects, System.Action{StateMachineOptions}, System.Reflection.Assembly[])"/>
/// overload and read by the machine registry when it builds a per-machine draft service.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/add-state-machines#options">SDK reference</seealso>
public sealed class StateMachineOptions
{
    /// <summary>The draft TTL a host has unless it sets <see cref="DraftTtl"/>: 30 days.</summary>
    public static readonly TimeSpan DefaultDraftTtl = TimeSpan.FromDays(30);

    /// <summary>The longest a user's draft lives unless the host sets <see cref="MaxDraftLifetime"/>: 90 days.</summary>
    public static readonly TimeSpan DefaultMaxDraftLifetime = TimeSpan.FromDays(90);

    /// <summary>The most drafts one user may hold unless the host sets <see cref="MaxDraftsPerUser"/>: 50.</summary>
    public const int DefaultMaxDraftsPerUser = 50;

    /// <summary>
    /// How long a user's draft survives without activity (a sliding window on the row's last update). Drafts
    /// expire after it by default: a background sweep on every host that registers machines deletes a user's draft
    /// idle past it, cancelling nothing and leaving alone a draft whose state is waiting on an invoked run or whose
    /// timer is still pending (up to <see cref="MaxDraftLifetime"/>), and a draft an effect is running on, and never
    /// touching a system-owned instance. It keeps an instance in a state only the server puts it in (a committed
    /// state, an effect's target, an invoked train's outcome target, a gated event's target), however long it is
    /// idle, as <see cref="MaxDraftLifetime"/> does: that is the record of what the server did. Its delete is conditional on the revision it read, so a draft written since
    /// is kept. A read never deletes. Defaults to <see cref="DefaultDraftTtl"/>; must be positive, and a host that sets
    /// it longer than <see cref="MaxDraftLifetime"/> refuses to start (a lifetime set below the default TTL is a hard
    /// cap and is accepted). <c>null</c>
    /// turns idle expiry off; <see cref="MaxDraftLifetime"/> still applies, so set that to <c>null</c> as well for a
    /// draft to live until it is discarded.
    /// </summary>
    public TimeSpan? DraftTtl
    {
        get;
        set
        {
            if (value is { } ttl)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
            field = value;
            DraftTtlSet = true;
        }
    } = DefaultDraftTtl;

    /// <summary>Whether the host set <see cref="DraftTtl"/> itself rather than keeping the default.</summary>
    internal bool DraftTtlSet { get; private set; }

    /// <summary>
    /// The most drafts one user may hold, counted across every machine. A <c>start</c>, or a save that creates a new
    /// draft, past it is refused with <c>too-many-drafts</c> and writes nothing; a retried start returns the instance
    /// it created, a save over a draft the user holds is not a new one, a draft idle past <see cref="DraftTtl"/> no
    /// longer counts, and system-owned instances never count. The count and the insert are one transaction, under a
    /// lock on the user, so concurrent starts cannot pass it together. Defaults to
    /// <see cref="DefaultMaxDraftsPerUser"/>; must be positive. <c>null</c> sets no cap. The InMemory provider has no
    /// transactions to hold the cap in: it does not apply the default, and a host that sets a cap on it refuses to
    /// start.
    /// </summary>
    public int? MaxDraftsPerUser
    {
        get;
        set
        {
            if (value is { } cap)
                ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);
            field = value;
            MaxDraftsPerUserSet = true;
        }
    } = DefaultMaxDraftsPerUser;

    /// <summary>Whether the host set <see cref="MaxDraftsPerUser"/> itself rather than keeping the default.</summary>
    internal bool MaxDraftsPerUserSet { get; private set; }

    /// <summary>Whether the provider cannot hold a cap (InMemory), so none is applied.</summary>
    internal bool CapUnavailable { get; set; }

    /// <summary>The cap a host applies: <see cref="MaxDraftsPerUser"/>, unless the provider cannot hold one.</summary>
    internal int? EffectiveMaxDraftsPerUser => CapUnavailable ? null : MaxDraftsPerUser;

    /// <summary>How often the draft sweep runs on a host that registers machines.</summary>
    internal TimeSpan DraftSweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often this host's outcome reconciler sweeps for invoked runs that have ended and applies each one's
    /// outcome to the state that invoked it. The default is 5 seconds. The sweep is the guarantee, not the usual
    /// path: the host that ran the train applies the outcome as soon as it is recorded, and on Postgres a
    /// notification wakes every host's reconciler sooner. It decides how long an outcome waits when both of those
    /// miss it (a crash after the run, a run the reaper failed, a cancel before dispatch), and how often the
    /// sweep runs while the notifications are not being heard. Must be positive.
    /// </summary>
    public TimeSpan InvokeOutcomeSweepInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often this host's outcome reconciler sweeps while it hears the provider's notification of invoked runs
    /// ending (Postgres): each notification delivers its run at once, and the reconciler sweeps in full whenever it
    /// subscribes again after losing them, so the sweep only has to catch what both miss. The default is 1 minute.
    /// Never shorter than <see cref="InvokeOutcomeSweepInterval"/>, which applies whenever the notifications are not
    /// heard. Must be positive.
    /// </summary>
    public TimeSpan InvokeOutcomeSweepIntervalWhileListening
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest a user's draft may live, counted from its creation, however its timers or its activity extend it.
    /// A draft idle past <see cref="DraftTtl"/> is kept while a timer of its state is still pending, so the state can
    /// move on by itself; this bounds that. The draft sweep deletes a user's draft created longer ago than this,
    /// whether or not it is idle or has a timer pending; one whose state waits on an invoked run has the run cancelled
    /// first. It keeps an instance in a state only the server puts it in (a committed state, an effect's target, an
    /// invoked train's outcome target, a gated event's target): that is the record of what the server did, and how
    /// long it stays is the host's to decide. It skips a draft an effect is running on until the effect settles. A
    /// draft written before its creation time was recorded counts its age from its last write. Defaults to
    /// <see cref="DefaultMaxDraftLifetime"/>; must be positive. A host that sets <see cref="DraftTtl"/> longer than
    /// this refuses to start, rather than the TTL raising it; set below the default TTL alone, it caps every draft.
    /// <c>null</c> sets no bound.
    /// </summary>
    public TimeSpan? MaxDraftLifetime
    {
        get;
        set
        {
            if (value is { } lifetime)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
            field = value;
        }
    } = DefaultMaxDraftLifetime;

    /// <summary>
    /// Whether <paramref name="machine"/> is served to clients through its own typed operations: its own type carries
    /// <see cref="Trax.Effect.Attributes.TraxMachineAttribute"/>, however the machine was registered. The attribute is
    /// not inherited, so a subclass of a served machine is not served by its base being so.
    /// </summary>
    internal static bool IsServed(IMachine machine) => IsMarked(machine.GetType());

    /// <summary>Whether <paramref name="machine"/> itself carries <see cref="Trax.Effect.Attributes.TraxMachineAttribute"/>.</summary>
    internal static bool IsMarked(Type machine) =>
        machine.IsDefined(typeof(Trax.Effect.Attributes.TraxMachineAttribute), inherit: false);

    /// <summary>
    /// The machines of <paramref name="machines"/> served to clients, those marked
    /// <see cref="Trax.Effect.Attributes.TraxMachineAttribute"/>, ordered by type full name, each type once: a machine
    /// registered twice is served once.
    /// </summary>
    internal static IReadOnlyList<IMachine> Served(IEnumerable<IMachine> machines) =>
        RegisteredMachines
            .Distinct(machines)
            .Where(IsServed)
            .OrderBy(m => m.GetType().FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The served machine types <paramref name="services"/> shows before the container is built: each
    /// <see cref="IMachine"/> registered by its type, or as an instance, whose type is marked. A machine registered
    /// through a factory is known only once the container builds it.
    /// </summary>
    internal static IReadOnlyList<Type> ServedIn(IServiceCollection services) =>
        services
            .Where(d => d.ServiceType == typeof(IMachine) && !d.IsKeyedService)
            .Select(d => d.ImplementationType ?? d.ImplementationInstance?.GetType())
            .OfType<Type>()
            .Where(IsMarked)
            .Distinct()
            .ToList();

    /// <summary>
    /// Serve the six generic <c>stateMachine</c> mutations (<c>startSnapshot</c>, <c>saveSnapshot</c>,
    /// <c>advanceSnapshot</c>, <c>loadSnapshot</c>, <c>sendSnapshot</c>, <c>discardSnapshot</c>) in GraphQL, each behind
    /// the authorization policy the options name.
    /// Without this call the trains behind them are still registered, so the mediator dispatches them for host code
    /// and a machine's own surface, but no client reaches them. While the opt-in is on, every user-owned machine is
    /// reachable through them, so each must declare a rule for every event, its start and its reads, under the same
    /// startup check as an exposed machine. Calling it without a
    /// <see cref="GenericStateMachineMutationOptions.Policy"/> refuses to start.
    /// </summary>
    /// <param name="configure">Sets the policy that gates the generic mutations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public StateMachineOptions ExposeGenericStateMachineMutations(
        Action<GenericStateMachineMutationOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GenericStateMachineMutationOptions();
        configure(options);
        GenericMutationsExposed = true;
        GenericMutationsPolicy = options.Policy;
        return this;
    }

    /// <summary>Whether <see cref="ExposeGenericStateMachineMutations"/> opted in to the generic mutations.</summary>
    internal bool GenericMutationsExposed { get; private set; }

    /// <summary>
    /// The policy <see cref="ExposeGenericStateMachineMutations"/> gates the generic mutations with, or <c>null</c>
    /// when it named none or was not called.
    /// </summary>
    internal string? GenericMutationsPolicy { get; private set; }

    /// <summary>
    /// How this host hears that an instance changed, to push it to the clients watching it (<c>machineChanged</c>,
    /// the operator feed). Defaults to <see cref="StateMachineChangeFeed.Notify"/>. It matters on Postgres only:
    /// SQLite and InMemory have no notifications and always poll.
    /// </summary>
    /// <remarks>
    /// <para><see cref="StateMachineChangeFeed.Notify"/> hears the database's notice of every committed write at once.
    /// Its cost is on the writes: every transaction that sends a Postgres <c>NOTIFY</c> takes one lock shared by the
    /// whole cluster and holds it through its commit, so concurrent writes to instances commit one at a time, together
    /// with every other notifying transaction (the work queue's and invoked runs' notices among them).</para>
    /// <para><see cref="StateMachineChangeFeed.Poll"/> opens no listening session and reads the watched rows' revisions
    /// once a second while something watches, as SQLite does: a change reaches a watcher up to a second later, and
    /// every host with an open watch reads those rows each second (an operator feed on one machine reads that machine's
    /// rows, one on every machine the whole table). It takes the snapshot notice off the writes only once the database
    /// stops sending it, and only database-wide: <c>ALTER DATABASE &lt;name&gt; SET trax.snapshot_notify = 'off'</c>
    /// (<c>'off'</c> in any case turns them off; write <c>'on'</c> or <c>'off'</c>, since any other value keeps them
    /// on). A write in the same transaction as another notice (a work queue entry, an invoked run) still takes the
    /// notify lock for that one. A host in this mode whose database still notifies starts, and logs a warning: polling
    /// is correct either way, and a fleet moving to it deploys every host first and turns the notices off after, so no
    /// host listening for them is left unnotified.</para>
    /// <para>The trigger reads the setting in the session that writes, so a role setting
    /// (<c>ALTER ROLE ... SET</c>), a connection string's <c>Options=-c trax.snapshot_notify=off</c> or a
    /// <c>SET LOCAL</c> turns the notices off for some writers only, and a host on
    /// <see cref="StateMachineChangeFeed.Notify"/> never hears their writes. None of them is supported while any host
    /// listens.</para>
    /// <para>A host left on <see cref="StateMachineChangeFeed.Notify"/> refuses to start when it can tell that some
    /// write sends no notice: the database's own setting is off, a role's is, its own session's is, or the trigger is
    /// not enabled for ordinary sessions (<c>tgenabled</c> other than <c>O</c> or <c>A</c>). It reads the database's
    /// setting from the catalog, not from its own session, and warns when its session reads otherwise. Another host's
    /// connection string or <c>SET LOCAL</c> is invisible to it. The check is made at startup: turning the notices off
    /// while such a host runs leaves its watchers silent until it restarts.</para>
    /// </remarks>
    public StateMachineChangeFeed ChangeFeed
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"{nameof(ChangeFeed)} is {nameof(StateMachineChangeFeed.Notify)} or {nameof(StateMachineChangeFeed.Poll)}."
                );
            field = value;
        }
    } = StateMachineChangeFeed.Notify;
}

/// <summary>How a host hears that a state-machine instance changed (<see cref="StateMachineOptions.ChangeFeed"/>).</summary>
public enum StateMachineChangeFeed
{
    /// <summary>
    /// On Postgres, one listening session per host hears the notice the database sends for every committed write.
    /// Changes arrive at once; every write pays for the notice.
    /// </summary>
    Notify,

    /// <summary>
    /// Every host with an open watch reads the watched rows' revisions once a second, as on SQLite. Once the database
    /// turns its snapshot notices off (<c>ALTER DATABASE ... SET trax.snapshot_notify = 'off'</c>), a snapshot write
    /// sends none, and takes the notify lock only when its transaction sends another notice.
    /// </summary>
    Poll,
}

/// <summary>The options of <see cref="StateMachineOptions.ExposeGenericStateMachineMutations"/>.</summary>
public sealed class GenericStateMachineMutationOptions
{
    /// <summary>
    /// The authorization policy a caller must satisfy to reach any of the six generic mutations. Required: the
    /// opt-in without one refuses to start. The host registers the policy with ASP.NET Core authorization as it
    /// does for a train's <c>[TraxAuthorize(Policy = ...)]</c>.
    /// </summary>
    public string? Policy { get; set; }
}
