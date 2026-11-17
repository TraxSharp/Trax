using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A system-owned wait: it starts in <c>Fetching</c>, which invokes <see cref="IInvokedStepTrain"/> and gives up
/// into <c>TimedOut</c> once it has waited the context's <c>patience</c> (between one minute and one hour). The
/// run's outcome enters <c>Fetched</c>, <c>Failed</c> or <c>Cancelled</c>; <c>Retry</c> enters <c>Fetching</c>
/// again.
/// </summary>
public sealed class WaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string MachineId = "timer-wait";

    public static readonly TimeSpan MinPatience = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxPatience = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(WaitState.Fetching, () => Context(InvokedStepModes.Ok))
            .SystemOwned();

        m.In(WaitState.Fetching)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>())
            )
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Failed)
            .OnCancelled(WaitState.Cancelled)
            .After<WaitContext>(c => c.Patience, MinPatience, MaxPatience)
            .GoTo(WaitState.TimedOut);

        m.In(WaitState.TimedOut).On(WaitTrigger.Retry).To(WaitState.Fetching);
        m.In(WaitState.Failed).On(WaitTrigger.Retry).To(WaitState.Fetching);
    }

    /// <summary>A context for a run in <paramref name="mode"/>, waiting <paramref name="patience"/> (ISO 8601).</summary>
    public static JsonObject Context(string mode, string patience = "PT5M", string? note = null) =>
        new()
        {
            ["mode"] = mode,
            ["note"] = note ?? Guid.NewGuid().ToString("N"),
            ["artifact"] = "",
            ["patience"] = patience,
        };
}
