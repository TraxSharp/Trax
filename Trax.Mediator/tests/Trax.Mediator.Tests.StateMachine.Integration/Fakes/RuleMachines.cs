using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

// The machines and trains of the startup comparison per entering event: each machine declares the rule of every
// event entering its invoking state, and the host compares each one with the train's [TraxAuthorize]. None of the
// machine ids contains another fake's id, so a startup message naming one never matches another.

public sealed record OperatorInput(string Source);

public interface IOperatorTrain : IServiceTrain<OperatorInput, JobOutput>;

/// <summary>Needs the <c>Operator</c> role.</summary>
[TraxAuthorize(Roles = "Operator")]
public class OperatorTrain : ServiceTrain<OperatorInput, JobOutput>, IOperatorTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<OperatorStep>().Resolve();
}

public sealed class OperatorStep : EffectJunction<OperatorInput, JobOutput>
{
    public override Task<JobOutput> Run(OperatorInput input) => Task.FromResult(new JobOutput());
}

public sealed record AuditedInput(string Source);

public interface IAuditedTrain : IServiceTrain<AuditedInput, JobOutput>;

/// <summary>Two role sets: a caller needs <c>Operator</c> and also <c>Auditor</c>.</summary>
[TraxAuthorize(Roles = "Operator")]
[TraxAuthorize(Roles = "Auditor")]
public class AuditedTrain : ServiceTrain<AuditedInput, JobOutput>, IAuditedTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<AuditedStep>().Resolve();
}

public sealed class AuditedStep : EffectJunction<AuditedInput, JobOutput>
{
    public override Task<JobOutput> Run(AuditedInput input) => Task.FromResult(new JobOutput());
}

public enum RuleState
{
    Idle,
    Running,
    Done,
    Failed,
    Cancelled,
}

public enum RuleTrigger
{
    Build,
    Rebuild,
    Stop,
}

/// <summary>The record a client starts a <see cref="RuleStageMachine{TTrain,TInput}"/> with.</summary>
public sealed record RuleStartInput(string Source);

/// <summary>
/// A user's machine: <c>Idle</c> --Build--> <c>Running</c>, which invokes <typeparamref name="TTrain"/>; with
/// <see cref="RebuildRule"/>, <c>Rebuild</c> enters <c>Running</c> too. With <see cref="StartRule"/>, the machine
/// starts in <c>Running</c> through its declared <c>start</c>, so the start enters the invoking state.
/// </summary>
public abstract class RuleStageMachine<TTrain, TInput> : Machine<RuleState, RuleTrigger>
    where TTrain : IServiceTrain<TInput, JobOutput>
{
    protected abstract string Id { get; }

    /// <summary>The rule of the <c>Build</c> event.</summary>
    protected abstract ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    );

    /// <summary>The rule of the <c>Rebuild</c> event, or <c>null</c> when the machine has none.</summary>
    protected virtual Func<
        ITransitionBuilder<RuleState, RuleTrigger>,
        ITransitionBuilder<RuleState, RuleTrigger>
    >? RebuildRule => null;

    /// <summary>The rule of the machine's <c>start</c>, or <c>null</c> when it declares none.</summary>
    protected virtual Action<IStartBuilder<RuleStartInput>>? StartRule => null;

    protected abstract TInput Input(string source);

    protected override void Configure(IMachineBuilder<RuleState, RuleTrigger> m)
    {
        var start = StartRule;
        m.Id(Id)
            .Version(1)
            .StartsAt(
                start is null ? RuleState.Idle : RuleState.Running,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (start is not null)
            m.Start(start);

        var idle = m.In(RuleState.Idle);
        BuildRule(idle.On(RuleTrigger.Build)).To(RuleState.Running);
        if (RebuildRule is { } rebuild)
            rebuild(idle.On(RuleTrigger.Rebuild)).To(RuleState.Running);

        m.In(RuleState.Running)
            .Invokes<TTrain, TInput, JobOutput>(ctx => Input(ctx["source"]!.GetValue<string>()))
            .OnDone(RuleState.Done)
            .OnFailed(RuleState.Failed)
            .OnCancelled(RuleState.Cancelled)
            .On(RuleTrigger.Stop)
            .AnyAuthenticated()
            .To(RuleState.Idle);
    }
}

/// <summary>A-25: an <c>Operator</c> event entering a state that invokes an <c>Operator</c> train.</summary>
public sealed class OperatorInvokesOperatorMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-operator-into-operator";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}

/// <summary>A-26: an <c>Operator</c> event entering a state that invokes an <c>admin</c> train.</summary>
public sealed class OperatorInvokesAdminMachine : RuleStageMachine<IAdminTrain, AdminInput>
{
    public const string MachineId = "rules-operator-into-admin";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override AdminInput Input(string source) => new(source);
}

/// <summary>
/// A-27, shaped like the topic map: <c>Build</c> requires <c>Operator</c> and <c>Rebuild</c> any authenticated
/// caller, and both enter the state invoking an <c>Operator</c> train.
/// </summary>
public sealed class EveryEnteringEventMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-every-entering-event";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override Func<
        ITransitionBuilder<RuleState, RuleTrigger>,
        ITransitionBuilder<RuleState, RuleTrigger>
    >? RebuildRule => rebuild => rebuild.AnyAuthenticated();

    protected override OperatorInput Input(string source) => new(source);
}

/// <summary>A-28: an event any <c>Operator</c> or <c>Viewer</c> may send, into an <c>Operator</c> train.</summary>
public sealed class AnyOfRoleMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-any-of-role";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator", "Viewer");

    protected override OperatorInput Input(string source) => new(source);
}

/// <summary>A-29: an event gated by a policy, into a train gated by a role.</summary>
public sealed class PolicyRuleMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-policy-into-role";
    public const string Policy = "can-build";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.RequiresPolicy(Policy);

    protected override OperatorInput Input(string source) => new(source);
}

/// <summary>A-30: an <c>Operator</c> event into a train that needs <c>Operator</c> and <c>Auditor</c>.</summary>
public sealed class OneRoleSetMachine : RuleStageMachine<IAuditedTrain, AuditedInput>
{
    public const string MachineId = "rules-one-role-set";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override AuditedInput Input(string source) => new(source);
}

/// <summary>
/// A-30: an event that requires <c>Operator</c> and, separately, <c>Auditor</c>, into a train that needs both.
/// </summary>
public sealed class BothRoleSetsMachine : RuleStageMachine<IAuditedTrain, AuditedInput>
{
    public const string MachineId = "rules-both-role-sets";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator").Requires("Auditor");

    protected override AuditedInput Input(string source) => new(source);
}

/// <summary>A-31: a start an <c>Operator</c> may make, into an initial state invoking an <c>Operator</c> train.</summary>
public sealed class OperatorStartMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-start-by-operator";
    protected override string Id => MachineId;

    protected override Action<IStartBuilder<RuleStartInput>>? StartRule =>
        s => s.Requires("Operator");

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}

/// <summary>A-31: a start any authenticated caller may make, into the same state.</summary>
public sealed class AnyoneStartMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-start-by-anyone";
    protected override string Id => MachineId;

    protected override Action<IStartBuilder<RuleStartInput>>? StartRule =>
        s => s.AnyAuthenticated();

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}
