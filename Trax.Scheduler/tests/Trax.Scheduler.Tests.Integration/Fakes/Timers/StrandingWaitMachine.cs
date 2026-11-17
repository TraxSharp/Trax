using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A system-owned wait whose run's failure strands it: <c>Fetching</c>'s <c>OnFailed</c> enters <c>Chained</c>,
/// whose run's input reads a field no context has, so that run cannot be queued and the instance stays in
/// <c>Fetching</c> with no live run. <c>Fetching</c> gives up into <c>TimedOut</c> after <see cref="Delay"/>.
/// </summary>
public sealed class StrandingWaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string MachineId = "timer-wait-strand";

    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(WaitState.Fetching, () => WaitMachine.Context(InvokedStepModes.Ok))
            .SystemOwned();

        m.In(WaitState.Fetching)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>())
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Chained)
            .OnCancelled(WaitState.Cancelled)
            .After(Delay)
            .GoTo(WaitState.TimedOut);

        m.In(WaitState.Chained)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["missing"]!.GetValue<string>(), "")
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Failed)
            .OnCancelled(WaitState.Cancelled);

        m.In(WaitState.Failed).On(WaitTrigger.Retry).To(WaitState.Fetching);
    }
}
