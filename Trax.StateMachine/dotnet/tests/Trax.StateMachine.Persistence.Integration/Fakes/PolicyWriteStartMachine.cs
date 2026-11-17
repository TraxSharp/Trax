using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// As <see cref="ForgedStartMachine"/>, but <c>Approve</c> requires a policy rather than a role: its write is still
/// one only the server makes, so the start must not reach it either.
/// </summary>
public sealed class PolicyWriteStartMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "policy-write-start";

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
            .RequiresPolicy(DeskRoles.Policy)
            .Reduce(Set((ApprovalContext c) => c.ApprovedBy).ToValue("approver"))
            .To(ApprovalState.Approved);

        m.In(ApprovalState.Approved).Context<ApprovalContext>();
    }
}
