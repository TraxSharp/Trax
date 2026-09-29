using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

// FOUR trains for ALL machines, under the `stateMachine` GraphQL namespace. No per-machine plumbing:
// the machine is a runtime argument the registry resolves. AddStateMachines registers these once.

/// <summary>Autosave mutation train (<c>stateMachine.saveSnapshot</c>): validates a client snapshot and persists it through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller.</summary>
[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Autosave: validate a client snapshot and persist it (soft path)."
)]
public class SaveSnapshot : ServiceTrain<SaveSnapshotInput, SaveSnapshotOutput>, ISaveSnapshot
{
    /// <summary>Chains the single <see cref="SaveSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SaveSnapshotOutput>> Junctions() =>
        Chain<SaveSnapshotJunction>().Resolve();
}

/// <summary>Authoritative advance mutation train (<c>stateMachine.advanceSnapshot</c>): re-drives the stored draft by one trigger through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller.</summary>
[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Advance: re-drive a stored draft by one trigger, server-side (authoritative path)."
)]
public class AdvanceSnapshot
    : ServiceTrain<AdvanceSnapshotInput, AdvanceSnapshotOutput>,
        IAdvanceSnapshot
{
    /// <summary>Chains the single <see cref="AdvanceSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, AdvanceSnapshotOutput>> Junctions() =>
        Chain<AdvanceSnapshotJunction>().Resolve();
}

/// <summary>Resume mutation train (<c>stateMachine.loadSnapshot</c>): reads the caller's stored draft through the machine's <see cref="ISnapshotDraftService"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller.</summary>
[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Load: resume the caller's stored draft."
)]
public class LoadSnapshot : ServiceTrain<LoadSnapshotInput, LoadSnapshotOutput>, ILoadSnapshot
{
    /// <summary>Chains the single <see cref="LoadSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, LoadSnapshotOutput>> Junctions() =>
        Chain<LoadSnapshotJunction>().Resolve();
}

/// <summary>Send mutation train (<c>stateMachine.sendSnapshot</c>): runs the machine's one irreversible effect exactly once through its <see cref="ISnapshotEffectRunner"/>. Registered once by <c>AddStateMachines</c> and shared by every machine; requires an authenticated caller.</summary>
[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Send: run a machine's one irreversible effect exactly once (state-gated + idempotent)."
)]
public class SendSnapshot : ServiceTrain<SendSnapshotInput, SendSnapshotOutput>, ISendSnapshot
{
    /// <summary>Chains the single <see cref="SendSnapshotJunction"/>, which does all the work.</summary>
    protected override Task<Either<Exception, SendSnapshotOutput>> Junctions() =>
        Chain<SendSnapshotJunction>().Resolve();
}
