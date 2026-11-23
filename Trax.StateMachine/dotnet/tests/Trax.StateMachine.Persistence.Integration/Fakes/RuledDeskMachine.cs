using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A desk whose events each declare a different rule: from <c>Open</c>, <c>OperatorOnly</c> requires Operator,
/// <c>OperatorOrAdmin</c> requires either, <c>PolicyOnly</c> requires <see cref="DeskRoles.Policy"/> and
/// <c>Anyone</c> any authenticated caller, each to <c>Done</c>; <c>Reopen</c> (Operator) goes back to <c>Open</c>.
/// Starting one requires Operator, and only its owner reads it.
/// </summary>
public sealed class RuledDeskMachine : Machine<DeskState, DeskTrigger>
{
    public const string Id = "ruled-desk";

    protected override void Configure(IMachineBuilder<DeskState, DeskTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(DeskState.Draft, () => new JsonObject { ["title"] = "desk" })
            .Start<DeskStart>(s =>
                s.Reduce(Set((DeskContext c) => c.Title).FromInput((DeskStart i) => i.Title))
                    .Requires(DeskRoles.Operator)
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(DeskState.Draft)
            .Context<DeskContext>()
            .On(DeskTrigger.Open)
            .AnyAuthenticated()
            .To(DeskState.Open);

        m.In(DeskState.Open)
            .Context<DeskContext>()
            .On(DeskTrigger.OperatorOnly)
            .Requires(DeskRoles.Operator)
            .To(DeskState.Done)
            .On(DeskTrigger.OperatorOrAdmin)
            .Requires(DeskRoles.Operator, DeskRoles.Admin)
            .To(DeskState.Done)
            .On(DeskTrigger.PolicyOnly)
            .RequiresPolicy(DeskRoles.Policy)
            .To(DeskState.Done)
            .On(DeskTrigger.Anyone)
            .AnyAuthenticated()
            .To(DeskState.Done);

        m.In(DeskState.Done)
            .Context<DeskContext>()
            .On(DeskTrigger.Reopen)
            .Requires(DeskRoles.Operator)
            .To(DeskState.Open);
    }

    /// <summary>A desk in <paramref name="state"/>, for seeding through the store.</summary>
    public static Snapshot In(DeskState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject { ["title"] = "desk" },
        };
}
