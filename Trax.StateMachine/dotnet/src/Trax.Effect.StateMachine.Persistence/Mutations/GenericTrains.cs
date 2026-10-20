using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

// SIX trains for ALL machines. No per-machine plumbing: the machine is a runtime argument the registry resolves.
// AddStateMachines registers their routes once, so the mediator dispatches them for host code and a machine's own
// surface. They carry no GraphQL attribute: they are served under the `stateMachine` namespace only when the host
// calls ExposeGenericStateMachineMutations, which registers each one's exposure with the policy that gates it. Each
// withholds its output, an instance's snapshot, from the lifecycle hooks: the broadcaster and SignalR never carry it.

/// <summary>Autosave mutation train (<c>stateMachine.saveSnapshot</c>): validates a client snapshot and persists it through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class SaveSnapshot : ServiceTrain<SaveSnapshotInput, SaveSnapshotOutput>, ISaveSnapshot
{
    /// <summary>Chains the single <see cref="SaveSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SaveSnapshotOutput>> Junctions() =>
        Chain<SaveSnapshotJunction>().Resolve();
}

/// <summary>Authoritative advance mutation train (<c>stateMachine.advanceSnapshot</c>): re-drives the stored draft by one trigger through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class AdvanceSnapshot
    : ServiceTrain<AdvanceSnapshotInput, AdvanceSnapshotOutput>,
        IAdvanceSnapshot
{
    /// <summary>Chains the single <see cref="AdvanceSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, AdvanceSnapshotOutput>> Junctions() =>
        Chain<AdvanceSnapshotJunction>().Resolve();
}

/// <summary>Resume mutation train (<c>stateMachine.loadSnapshot</c>): reads the caller's stored draft through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class LoadSnapshot : ServiceTrain<LoadSnapshotInput, LoadSnapshotOutput>, ILoadSnapshot
{
    /// <summary>Chains the single <see cref="LoadSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, LoadSnapshotOutput>> Junctions() =>
        Chain<LoadSnapshotJunction>().Resolve();
}

/// <summary>Send mutation train (<c>stateMachine.sendSnapshot</c>): runs the machine's one irreversible effect exactly once through its <see cref="ISnapshotEffectRunner"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class SendSnapshot : ServiceTrain<SendSnapshotInput, SendSnapshotOutput>, ISendSnapshot
{
    /// <summary>Chains the single <see cref="SendSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SendSnapshotOutput>> Junctions() =>
        Chain<SendSnapshotJunction>().Resolve();
}

/// <summary>Start mutation train (<c>stateMachine.startSnapshot</c>): creates a new instance from the machine's declared start through its <see cref="ISnapshotDraftService"/>, under an id the server mints. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class StartSnapshot : ServiceTrain<StartSnapshotInput, StartSnapshotOutput>, IStartSnapshot
{
    /// <summary>Chains the single <see cref="StartSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, StartSnapshotOutput>> Junctions() =>
        Chain<StartSnapshotJunction>().Resolve();
}

/// <summary>Discard mutation train (<c>stateMachine.discardSnapshot</c>): deletes one of the caller's instances through its machine's <see cref="ISnapshotDraftService"/>, cancelling a run its state invoked first. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller. Served in GraphQL only behind <c>ExposeGenericStateMachineMutations</c>.</summary>
[TraxAuthorize]
[OutputWithheldFromLifecycleHooks]
public class DiscardSnapshot
    : ServiceTrain<DiscardSnapshotInput, DiscardSnapshotOutput>,
        IDiscardSnapshot
{
    /// <summary>Chains the single <see cref="DiscardSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, DiscardSnapshotOutput>> Junctions() =>
        Chain<DiscardSnapshotJunction>().Resolve();
}
