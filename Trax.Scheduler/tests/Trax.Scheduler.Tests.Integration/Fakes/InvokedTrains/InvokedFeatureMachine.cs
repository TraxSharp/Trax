using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public enum FeatureState
{
    Idle,
    Deciding,
    Shortening,
    Done,
    Failed,
    Cancelled,
}

public enum FeatureTrigger
{
    Decide,
    Shorten,
    Retry,
}

/// <summary>
/// A user's machine whose states invoke trains using other Trax features: <c>Deciding</c> invokes
/// <see cref="IInvokedDecidingTrain"/>, which asks a decider, and <c>Shortening</c> invokes
/// <see cref="IInvokedShortCircuitTrain"/>. Either reduces the run's artifact into the context on <c>Done</c>;
/// <c>Retry</c> enters <c>Deciding</c> again from <c>Failed</c>. Its contexts are <see cref="StepMachine.Context"/>'s.
/// </summary>
public sealed class InvokedFeatureMachine : Machine<FeatureState, FeatureTrigger>
{
    public const string MachineId = "invoked-feature-user";

    private static readonly Reduction KeepArtifact = Set((StepContext c) => c.Artifact)
        .FromInput((InvokedStepOutput o) => o.Artifact);

    protected override void Configure(IMachineBuilder<FeatureState, FeatureTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(FeatureState.Idle, () => StepMachine.Context("ok"));

        m.In(FeatureState.Idle).On(FeatureTrigger.Decide).To(FeatureState.Deciding);
        m.In(FeatureState.Idle).On(FeatureTrigger.Shorten).To(FeatureState.Shortening);

        m.In(FeatureState.Deciding)
            .Invokes<IInvokedDecidingTrain, InvokedStepInput, InvokedStepOutput>(Input)
            .OnDone(FeatureState.Done, reduce: KeepArtifact)
            .OnFailed(FeatureState.Failed)
            .OnCancelled(FeatureState.Cancelled);

        m.In(FeatureState.Shortening)
            .Invokes<IInvokedShortCircuitTrain, InvokedStepInput, InvokedStepOutput>(Input)
            .OnDone(FeatureState.Done, reduce: KeepArtifact)
            .OnFailed(FeatureState.Failed)
            .OnCancelled(FeatureState.Cancelled);

        m.In(FeatureState.Failed).On(FeatureTrigger.Retry).To(FeatureState.Deciding);
    }

    private static InvokedStepInput Input(System.Text.Json.Nodes.JsonObject ctx) =>
        new(ctx["mode"]!.GetValue<string>(), ctx["note"]!.GetValue<string>());
}
