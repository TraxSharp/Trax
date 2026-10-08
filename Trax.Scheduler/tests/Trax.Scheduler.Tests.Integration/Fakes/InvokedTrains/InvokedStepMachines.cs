using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public enum StepState
{
    Idle,
    Running,
    Chained,
    Done,
    Failed,
    Cancelled,
}

public enum StepTrigger
{
    Go,
    Stop,
    Retry,
    Continue,
}

/// <summary>The context the steps read and the outcome reduces into.</summary>
public sealed record StepContext
{
    public string Mode { get; init; } = "";

    public string Note { get; init; } = "";

    public string Artifact { get; init; } = "";

    public string Pad { get; init; } = "";
}

/// <summary>
/// <c>Idle</c> --Go--> <c>Running</c>, which invokes <see cref="IInvokedStepTrain"/> with the context's mode and note.
/// Its output routes to <c>Done</c> when it is accepted, reducing the artifact into the context; a failure goes to
/// <c>Failed</c>, a cancel to <c>Cancelled</c>, and <c>Stop</c> leaves it. <c>Retry</c> enters <c>Running</c> again.
/// <c>Chained</c> invokes the next run. A system-owned machine starts in <c>Running</c>, and an output that asks to
/// chain enters <c>Chained</c> directly; a user-owned machine may not chain through an outcome, so its user enters
/// <c>Chained</c> from <c>Done</c> with <c>Continue</c>.
/// </summary>
public abstract class StepMachine : Machine<StepState, StepTrigger>
{
    private static readonly Reduction KeepArtifact = Set((StepContext c) => c.Artifact)
        .FromInput((InvokedStepOutput o) => o.Artifact);

    protected abstract string Id { get; }

    protected virtual bool System => false;

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(System ? StepState.Running : StepState.Idle, () => Context("ok"));
        if (System)
            m.SystemOwned();

        m.In(StepState.Idle).On(StepTrigger.Go).To(StepState.Running);

        m.In(StepState.Running)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>())
            )
            .OnDone(
                System ? StepState.Chained : StepState.Done,
                when: Input((InvokedStepOutput o) => o.Chain).IsTrue()
            )
            .OnDone(
                StepState.Done,
                when: Input((InvokedStepOutput o) => o.Accepted).IsTrue(),
                reduce: KeepArtifact
            )
            .OnFailed(StepState.Failed)
            .OnCancelled(StepState.Cancelled)
            .On(StepTrigger.Stop)
            .To(StepState.Idle);

        m.In(StepState.Chained)
            .Invokes<IInvokedStepTrain, InvokedStepInput, InvokedStepOutput>(ctx =>
                new(InvokedStepModes.Ok, "next:" + ctx["note"]!.GetValue<string>())
            )
            .OnDone(
                StepState.Done,
                when: Input((InvokedStepOutput o) => o.Accepted).IsTrue(),
                reduce: KeepArtifact
            )
            .OnFailed(StepState.Failed)
            .OnCancelled(StepState.Cancelled);

        if (!System)
            m.In(StepState.Done).On(StepTrigger.Continue).To(StepState.Chained);

        m.In(StepState.Failed).On(StepTrigger.Retry).To(StepState.Running);
        m.In(StepState.Cancelled).On(StepTrigger.Retry).To(StepState.Running);
    }

    /// <summary>A context for a run in <paramref name="mode"/>, with a unique note and an optional pad.</summary>
    public static JsonObject Context(string mode, string? note = null, int padBytes = 0) =>
        new()
        {
            ["mode"] = mode,
            ["note"] = note ?? Guid.NewGuid().ToString("N"),
            ["artifact"] = "",
            ["pad"] = new string('p', padBytes),
        };

    /// <summary>A snapshot of the user-owned machine in <paramref name="state"/> as a client sends it.</summary>
    public static string Json(string machine, string state, JsonObject context) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = context,
        }.ToJsonString();
}

/// <summary>A user's machine.</summary>
public sealed class UserStepMachine : StepMachine
{
    public const string MachineId = "invoked-step-user";
    protected override string Id => MachineId;
}

/// <summary>A system-owned machine, whose instances start in <c>Running</c>.</summary>
public sealed class SystemStepMachine : StepMachine
{
    public const string MachineId = "invoked-step-system";
    protected override string Id => MachineId;
    protected override bool System => true;
}
