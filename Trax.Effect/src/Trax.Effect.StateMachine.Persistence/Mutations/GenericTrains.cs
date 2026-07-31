using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

// FOUR trains for ALL machines, under the `stateMachine` GraphQL namespace. No per-machine plumbing:
// the machine is a runtime argument the registry resolves. AddTraxStateMachines registers these once.

[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Autosave: validate a client snapshot and persist it (soft path)."
)]
public class SaveSnapshot : ServiceTrain<SaveSnapshotInput, SaveSnapshotOutput>, ISaveSnapshot
{
    protected override Task<Either<Exception, SaveSnapshotOutput>> Junctions() =>
        Chain<SaveSnapshotJunction>().Resolve();
}

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
    protected override Task<Either<Exception, AdvanceSnapshotOutput>> Junctions() =>
        Chain<AdvanceSnapshotJunction>().Resolve();
}

[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Load: resume the caller's stored draft."
)]
public class LoadSnapshot : ServiceTrain<LoadSnapshotInput, LoadSnapshotOutput>, ILoadSnapshot
{
    protected override Task<Either<Exception, LoadSnapshotOutput>> Junctions() =>
        Chain<LoadSnapshotJunction>().Resolve();
}

[TraxAuthorize]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = "stateMachine",
    Description = "Send: run a machine's one irreversible effect exactly once (state-gated + idempotent)."
)]
public class SendSnapshot : ServiceTrain<SendSnapshotInput, SendSnapshotOutput>, ISendSnapshot
{
    protected override Task<Either<Exception, SendSnapshotOutput>> Junctions() =>
        Chain<SendSnapshotJunction>().Resolve();
}
