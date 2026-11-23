using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// A system-owned machine whose run's failure cannot be applied: <c>Running</c>'s <c>OnFailed</c> enters
/// <c>Chained</c>, whose run's input reads a field no context has, so that run cannot be queued and a failed run
/// strands the instance in <c>Running</c> with no live run.
/// </summary>
public sealed class StrandingStepMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "invoked-step-strand";

    private static readonly Reduction KeepArtifact = Set((StepContext c) => c.Artifact)
        .FromInput((InvokedStepOutput o) => o.Artifact);

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(StepState.Running, () => StepMachine.Context(InvokedStepModes.Ok))
            .SystemOwned();

        m.In(StepState.Running)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(
                    ctx["mode"].NotNull().GetValue<string>(),
                    ctx["note"].NotNull().GetValue<string>()
                )
            )
            .OnDone(
                StepState.Done,
                when: Input((InvokedStepOutput o) => o.Accepted).IsTrue(),
                reduce: KeepArtifact
            )
            .OnFailed(StepState.Chained)
            .OnCancelled(StepState.Cancelled)
            .On(StepTrigger.Stop)
            .To(StepState.Idle);

        m.In(StepState.Chained)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["missing"].NotNull().GetValue<string>(), "")
            )
            .OnDone(StepState.Done, when: Input((InvokedStepOutput o) => o.Accepted).IsTrue())
            .OnFailed(StepState.Failed)
            .OnCancelled(StepState.Cancelled);

        m.In(StepState.Idle).On(StepTrigger.Go).To(StepState.Running);
        m.In(StepState.Failed).On(StepTrigger.Retry).To(StepState.Running);
    }
}
