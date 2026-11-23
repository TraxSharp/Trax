using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>
/// An agent's assignment: what a client drives, through the twin and the operations the host serves for it. Each
/// agent owns its own instances. It starts one with the ticket a claim gave it, sends its lane's evidence, and the
/// state that evidence enters runs the lane's check train on the server, which runs the evidence itself and, only if
/// it holds, moves the ticket. The agent never moves the ticket, and the caller rule on each event keeps each lane
/// to the role that works it.
/// <code>
/// Working, Rejected --Triage------> CheckingTriage --+
///                   --SubmitRepro-> CheckingRepro  --+
///                   --Close-------> CheckingClose  --+--accepted--> Accepted (Closed for Close)
///                   --SubmitFix---> CheckingFix    --+--refused---> Rejected
///                   --SubmitChecks> CheckingQa     --+
///                   --Approve, SendBack-> CheckingReview
/// </code>
/// </summary>
[TraxMachine]
public sealed class AssignmentMachine : Machine<AssignmentState, AssignmentTrigger>
{
    public const string MachineId = "assignment";

    private static readonly Rule Accepted = Input((CheckResult r) => r.Accepted).IsTrue();

    private static readonly Reduction TakeVerdict = SetAll(
        Set((AssignmentContext c) => c.Verdict).FromInput((CheckResult r) => r.Message),
        Set((AssignmentContext c) => c.FailedCases).FromInput((CheckResult r) => r.FailedCases)
    );

    private static readonly Reduction CheckDidNotFinish = Set((AssignmentContext c) => c.Verdict)
        .ToValue("the check did not finish; send again");

    protected override void Configure(IMachineBuilder<AssignmentState, AssignmentTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(AssignmentState.Working, () => new JsonObject { ["ticketId"] = "unknown" })
            .Start<AssignmentStart>(s =>
                s.Reduce(
                        Set((AssignmentContext c) => c.TicketId)
                            .FromInput((AssignmentStart i) => i.TicketId)
                    )
                    .Requires(BoardRoles.AnyLane.Split(','))
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        Sends(m.In(AssignmentState.Working).Context<AssignmentContext>());
        Sends(m.In(AssignmentState.Rejected).Context<AssignmentContext>());

        Checks<ITriageTrain, TriageCheck>(m, AssignmentState.CheckingTriage, TriageCheck.From);
        Checks<IReproduceTrain, ReproCheck>(m, AssignmentState.CheckingRepro, ReproCheck.From);
        Checks<ICloseTrain, CloseCheck>(m, AssignmentState.CheckingClose, CloseCheck.From);
        Checks<IVerifyFixTrain, FixCheck>(m, AssignmentState.CheckingFix, FixCheck.From);
        Checks<IQaTrain, QaCheck>(m, AssignmentState.CheckingQa, QaCheck.From);
        Checks<IReviewTrain, ReviewCheck>(m, AssignmentState.CheckingReview, ReviewCheck.From);

        m.In(AssignmentState.Accepted).Context<AssignmentContext>();
        m.In(AssignmentState.Closed).Context<AssignmentContext>();
    }

    // What an agent may send while it holds the ticket, or after a refusal: its lane's evidence, gated by its lane's
    // role, so a fixer cannot approve and a reproducer cannot fix.
    private static void Sends(IStateBuilder<AssignmentState, AssignmentTrigger> state) =>
        state
            .On(AssignmentTrigger.Triage)
            .Requires(BoardRoles.Triage)
            .WithInput<TriageInput>()
            .Reduce(Set((AssignmentContext c) => c.Area).FromInput((TriageInput i) => i.Area))
            .To(AssignmentState.CheckingTriage)
            .On(AssignmentTrigger.SubmitRepro)
            .Requires(BoardRoles.Reproducer)
            .WithInput<ReproCase>()
            .Reduce(
                SetAll(
                    Set((AssignmentContext c) => c.Subtotal).FromInput((ReproCase i) => i.Subtotal),
                    Set((AssignmentContext c) => c.ExpectedShipping)
                        .FromInput((ReproCase i) => i.ExpectedShipping)
                )
            )
            .To(AssignmentState.CheckingRepro)
            .On(AssignmentTrigger.Close)
            .Requires(BoardRoles.Reproducer)
            .When(Field((AssignmentContext c) => c.FailedCases).AtLeast(2))
            .Because("Close needs at least two cases the server ran that did not reproduce it.")
            .To(AssignmentState.CheckingClose)
            .On(AssignmentTrigger.SubmitFix)
            .Requires(BoardRoles.Fixer)
            .WithInput<RulesPatch>()
            .Reduce(
                SetAll(
                    Set((AssignmentContext c) => c.FreeFrom)
                        .FromInput((RulesPatch i) => i.FreeFrom),
                    Set((AssignmentContext c) => c.Inclusive)
                        .FromInput((RulesPatch i) => i.Inclusive)
                )
            )
            .To(AssignmentState.CheckingFix)
            .On(AssignmentTrigger.SubmitChecks)
            .Requires(BoardRoles.Qa)
            .WithInput<QaCases>()
            .When(Input((QaCases i) => i.Subtotals).CountAtLeast(1))
            .Because("Send at least one subtotal to check.")
            .Reduce(Set((AssignmentContext c) => c.Subtotals).FromInput((QaCases i) => i.Subtotals))
            .To(AssignmentState.CheckingQa)
            .On(AssignmentTrigger.Approve)
            .Requires(BoardRoles.Reviewer)
            .Reduce(Set((AssignmentContext c) => c.Decision).ToValue("approve"))
            .To(AssignmentState.CheckingReview)
            .On(AssignmentTrigger.SendBack)
            .Requires(BoardRoles.Reviewer)
            .WithInput<SendBackInput>()
            .Reduce(
                SetAll(
                    Set((AssignmentContext c) => c.Decision).ToValue("send-back"),
                    Set((AssignmentContext c) => c.Reason).FromInput((SendBackInput i) => i.Reason)
                )
            )
            .To(AssignmentState.CheckingReview);

    // A checking state runs the lane's check train on what the agent sent. Only its outcome leaves the state.
    private static void Checks<TTrain, TInput>(
        IMachineBuilder<AssignmentState, AssignmentTrigger> m,
        AssignmentState state,
        Func<JsonObject, TInput> input
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, CheckResult> =>
        m.In(state)
            .Context<AssignmentContext>()
            .Invokes<TTrain, TInput, CheckResult>(input)
            .OnDone(
                state == AssignmentState.CheckingClose
                    ? AssignmentState.Closed
                    : AssignmentState.Accepted,
                when: Accepted,
                reduce: TakeVerdict
            )
            .OnDone(AssignmentState.Rejected, reduce: TakeVerdict)
            .OnFailed(AssignmentState.Rejected, reduce: CheckDidNotFinish)
            .OnCancelled(AssignmentState.Rejected, reduce: CheckDidNotFinish);
}
