using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public enum StepState
{
    Idle,
    Running,
    Done,
    Failed,
    Cancelled,
}

public enum StepTrigger
{
    Go,
    Stop,
    Retry,
}

/// <summary>
/// One stage: <c>Idle</c> --Go--> <c>Running</c>, which invokes <typeparamref name="TTrain"/> with the context's
/// <c>source</c>; <c>Stop</c> leaves it, and its outcome goes to <c>Done</c>, <c>Failed</c> or <c>Cancelled</c>,
/// from which <c>Retry</c> enters <c>Running</c> again. A system-owned stage starts in <c>Running</c>.
/// </summary>
public abstract class StageMachine<TTrain, TInput, TOutput> : Machine<StepState, StepTrigger>
    where TTrain : IServiceTrain<TInput, TOutput>
{
    protected abstract string Id { get; }

    protected virtual bool System => false;

    protected virtual int? Limit => null;

    protected abstract TInput Input(string source);

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                System ? StepState.Running : StepState.Idle,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (System)
            m.SystemOwned();
        if (Limit is { } limit)
            m.InvokedRunLimit(limit);

        m.In(StepState.Idle).On(StepTrigger.Go).To(StepState.Running);

        m.In(StepState.Running)
            .Invokes<TTrain, TInput, TOutput>(ctx => Input(ctx["source"]!.GetValue<string>()))
            .OnDone(StepState.Done)
            .OnFailed(StepState.Failed)
            .OnCancelled(StepState.Cancelled)
            .On(StepTrigger.Stop)
            .To(StepState.Idle);

        m.In(StepState.Failed).On(StepTrigger.Retry).To(StepState.Running);
        m.In(StepState.Cancelled).On(StepTrigger.Retry).To(StepState.Running);
    }

    /// <summary>A snapshot of the machine named <paramref name="id"/> as a client sends it.</summary>
    public static string Json(string id, string state, string source = "repo") =>
        new JsonObject
        {
            ["machine"] = id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject { ["source"] = source },
        }.ToJsonString();
}

/// <summary>A user's machine invoking a train it may.</summary>
public sealed class GoodMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "good-stage";
    protected override string Id => MachineId;

    protected override GoodInput Input(string source) => new(source);
}

/// <summary>A user's machine with a live-run cap of 2.</summary>
public sealed class CappedMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "capped-stage";
    protected override string Id => MachineId;
    protected override int? Limit => 2;

    protected override GoodInput Input(string source) => new(source);
}

/// <summary>A system-owned machine whose initial state invokes a train it may.</summary>
public sealed class PartitionMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "partition-stage";
    protected override string Id => MachineId;
    protected override bool System => true;

    protected override GoodInput Input(string source) => new(source);
}

public sealed class PlainJunctionMachine : StageMachine<IPlainJunctionTrain, PlainInput, JobOutput>
{
    protected override string Id => "plain-junction-stage";

    protected override PlainInput Input(string source) => new(source);
}

public sealed class ContractMachine : StageMachine<IContractTrain, ContractInput, JobOutput>
{
    protected override string Id => "contract-stage";

    protected override ContractInput Input(string source) => new(source);
}

public sealed class BareMachine : StageMachine<IBareTrain, BareInput, JobOutput>
{
    protected override string Id => "bare-stage";

    protected override BareInput Input(string source) => new(source);
}

public sealed class RefusedChainMachine : StageMachine<IRefusedChainTrain, RefusedInput, JobOutput>
{
    protected override string Id => "refused-chain-stage";

    protected override RefusedInput Input(string source) => new(source);
}

public sealed class ForkMachine : StageMachine<IForkTrain, ForkInput, JobOutput>
{
    protected override string Id => "fork-stage";

    protected override ForkInput Input(string source) => new(source);
}

public sealed class HookedMachine : StageMachine<IHookedTrain, HookedInput, JobOutput>
{
    protected override string Id => "hooked-stage";

    protected override HookedInput Input(string source) => new(source);
}

public sealed class AdminMachine : StageMachine<IAdminTrain, AdminInput, JobOutput>
{
    protected override string Id => "admin-stage";

    protected override AdminInput Input(string source) => new(source);
}

public sealed class LoudMachine : StageMachine<ILoudTrain, LoudInput, JobOutput>
{
    protected override string Id => "loud-stage";

    protected override LoudInput Input(string source) => new(source);
}

/// <summary>A system-owned machine invoking a train that needs a role.</summary>
public sealed class SystemAdminMachine : StageMachine<IAdminTrain, AdminInput, JobOutput>
{
    protected override string Id => "system-admin-stage";
    protected override bool System => true;

    protected override AdminInput Input(string source) => new(source);
}

public sealed class SecretMachine : StageMachine<ISecretTrain, SecretInput, SecretOutput>
{
    protected override string Id => "secret-stage";

    protected override SecretInput Input(string source) => new(source);
}

public enum ChainState
{
    Idle,
    Fetching,
    Fetched,
    Embedding,
    Done,
    Failed,
    Cancelled,
}

public enum ChainTrigger
{
    Go,
    Continue,
    Retry,
}

/// <summary>
/// Two stages: <c>Fetching</c> and <c>Embedding</c> each invoke <see cref="IGoodTrain"/>. When
/// <see cref="ChainedByOutcome"/>, <c>Fetching</c>'s <c>OnDone</c> enters <c>Embedding</c> directly, so the outcome
/// queues the next run; otherwise it enters <c>Fetched</c>, and the user's <c>Continue</c> enters <c>Embedding</c>.
/// </summary>
public abstract class ChainStageMachine : Machine<ChainState, ChainTrigger>
{
    protected abstract string Id { get; }

    protected virtual bool System => false;

    protected virtual bool ChainedByOutcome => true;

    protected override void Configure(IMachineBuilder<ChainState, ChainTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                System ? ChainState.Fetching : ChainState.Idle,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (System)
            m.SystemOwned();

        m.In(ChainState.Idle).On(ChainTrigger.Go).To(ChainState.Fetching);

        m.In(ChainState.Fetching)
            .Invokes<IGoodTrain, GoodInput, JobOutput>(ctx =>
                new(ctx["source"]!.GetValue<string>())
            )
            .OnDone(ChainedByOutcome ? ChainState.Embedding : ChainState.Fetched)
            .OnFailed(ChainState.Failed)
            .OnCancelled(ChainState.Cancelled);

        m.In(ChainState.Fetched).On(ChainTrigger.Continue).To(ChainState.Embedding);

        m.In(ChainState.Embedding)
            .Invokes<IGoodTrain, GoodInput, JobOutput>(ctx =>
                new(ctx["source"]!.GetValue<string>())
            )
            .OnDone(ChainState.Done)
            .OnFailed(ChainState.Failed)
            .OnCancelled(ChainState.Cancelled);

        m.In(ChainState.Failed).On(ChainTrigger.Retry).To(ChainState.Fetching);
    }
}

/// <summary>A user's machine whose first stage's outcome enters the second stage, which invokes a train.</summary>
public sealed class UserChainMachine : ChainStageMachine
{
    public const string MachineId = "user-chain-stage";
    protected override string Id => MachineId;
}

/// <summary>The same chain, system-owned.</summary>
public sealed class SystemChainMachine : ChainStageMachine
{
    public const string MachineId = "system-chain-stage";
    protected override string Id => MachineId;
    protected override bool System => true;
}

/// <summary>A user's machine that chains its stages through the user's <c>Continue</c>.</summary>
public sealed class UserContinueMachine : ChainStageMachine
{
    public const string MachineId = "user-continue-stage";
    protected override string Id => MachineId;
    protected override bool ChainedByOutcome => false;
}
