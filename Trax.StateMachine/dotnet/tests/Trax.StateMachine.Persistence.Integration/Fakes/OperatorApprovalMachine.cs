using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// An approval only an operator gives: <c>Pending -Approve(Operator)-&gt; Approved</c>, with an owner-only discard.
/// <c>Approved</c> records a decision the server's gate let through.
/// </summary>
public sealed class OperatorApprovalMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "operator-approval";

    protected override void Configure(IMachineBuilder<ApprovalState, ApprovalTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ApprovalState.Pending, () => new JsonObject { ["approvedBy"] = "" })
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
