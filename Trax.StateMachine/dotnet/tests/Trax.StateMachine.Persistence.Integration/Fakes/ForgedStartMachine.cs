using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A start that reaches a field only a gated event writes: <c>Approve</c> (Operator) sets <c>approvedBy</c>, and the
/// start input sets it too, which refuses the build.
/// </summary>
public class ForgedStartMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "forged-start";

    protected override void Configure(IMachineBuilder<ApprovalState, ApprovalTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ApprovalState.Pending, () => new JsonObject { ["approvedBy"] = "" })
            .Start<ApprovalStart>(s =>
                s.Reduce(
                        Set((ApprovalContext c) => c.ApprovedBy)
                            .FromInput((ApprovalStart i) => i.ApprovedBy)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ApprovalState.Pending)
            .Context<ApprovalContext>()
            .On(ApprovalTrigger.Approve)
            .Requires(DeskRoles.Operator)
            .Reduce(Set((ApprovalContext c) => c.ApprovedBy).ToValue("operator"))
            .To(ApprovalState.Approved);

        m.In(ApprovalState.Approved).Context<ApprovalContext>();
    }
}
