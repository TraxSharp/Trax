using Trax.Api.DTOs;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

// The operator's one action on a state-machine instance. It calls the operations service method
// the dashboard's Cancel button calls (central docs/0022) and sits under the operations field, so
// the host's operations gate decides who may call it (central docs/0046).
public partial class OperationsMutations
{
    /// <summary>
    /// Cancels a system-owned state-machine instance's live train run, and the instance then
    /// moves through its state's <c>OnCancelled</c> edge. A run still queued is cancelled before
    /// it starts and, when this host registers the machine, the instance moves in this call
    /// (<c>outcome: MOVED</c>, <c>state</c> the state it entered); otherwise a host that does
    /// moves it (<c>RUN_CANCELLED</c>). A dispatched run has its cancel requested: it stops at its
    /// next junction and the instance moves when it ends (<c>CANCEL_REQUESTED</c>). Refused with
    /// <c>success: false</c> and a typed <c>outcome</c>, changing nothing: a user-owned instance
    /// (<c>USER_OWNED</c>; operators see users' drafts read-only), no such system instance
    /// (<c>NOT_FOUND</c>), a state that waits on no run (<c>NO_LIVE_RUN</c>), and a run that has
    /// already ended (<c>RUN_ENDED</c>). The message is the dashboard's, word for word.
    /// </summary>
    /// <param name="machine">The machine's id.</param>
    /// <param name="ownerKind">Who owns the instance; only <c>SYSTEM</c> can be cancelled.</param>
    /// <param name="id">The instance id.</param>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the request.</param>
    public async Task<MachineInstanceCancelResponse> CancelMachineInstance(
        string machine,
        SnapshotOwnerKind ownerKind,
        Guid id,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(machine))
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("machine must name a machine.")
                    .SetCode(Validation.RunIdArgument.ErrorCode)
                    .Build()
            );

        return MachineInstanceCancelResponse.From(
            await operationsService.CancelMachineInstanceAsync(
                new MachineInstanceKey(machine, ownerKind, id),
                ct
            )
        );
    }
}
