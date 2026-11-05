using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

public enum BadgeState
{
    Issued,
    Returned,
}

public enum BadgeTrigger
{
    Return,
}

public sealed record BadgeContext
{
    public string? Holder { get; init; }
}

/// <summary>
/// A machine whose push needs a policy as well as ownership: only an owner who satisfies
/// <see cref="WatchPolicy"/> may watch an instance. Abstract, so only a host that serves it
/// registers it, and that host registers the policy.
/// </summary>
public abstract class BadgeMachine : Machine<BadgeState, BadgeTrigger>
{
    public const string MachineId = "badge";

    /// <summary>The policy the push requires.</summary>
    public const string WatchPolicy = "BadgeWatchers";

    protected override void Configure(IMachineBuilder<BadgeState, BadgeTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(BadgeState.Issued, () => new JsonObject());
        m.Start<EmptyStart>(s => s.Reduce(Rules.Keep()).AnyAuthenticated());
        m.In(BadgeState.Returned).Context<BadgeContext>();
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly().RequiresPolicy(WatchPolicy));
        m.Discard(r => r.OwnerOnly());
        m.In(BadgeState.Issued)
            .Context<BadgeContext>()
            .On(BadgeTrigger.Return)
            .AnyAuthenticated()
            .To(BadgeState.Returned);
    }
}
