using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Delete one of the caller's instances. Infrastructure chained by <see cref="DiscardSnapshot"/>; not intended to be called directly.</summary>
internal sealed class DiscardSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    IServiceProvider? services = null
) : Junction<DiscardSnapshotInput, DiscardSnapshotOutput>
{
    /// <summary>
    /// Discards the current user's instance. A missing one, or another user's, is answered as discarded. Refusals
    /// are returned as a <see cref="SnapshotProblem"/>: <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, or the code
    /// of a <see cref="DiscardOutcome.Rejected"/>.
    /// </summary>
    public override async Task<DiscardSnapshotOutput> Run(DiscardSnapshotInput input)
    {
        if (SnapshotOwner.Key(principal) is not { } userKey)
            return Problem(
                RefusalCode.Unauthenticated,
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is not { } service)
            return Problem(
                RefusalCode.UnknownMachine,
                $"No registered machine named '{input.Machine}'."
            );

        var outcome = await service
            .Discard(
                CallerRules.Client(userKey, CallerRules.Caller(services)),
                input.Id,
                input.RequestId,
                CancellationToken
            )
            .ConfigureAwait(false);

        return outcome switch
        {
            DiscardOutcome.Discarded => new DiscardSnapshotOutput { Discarded = true },
            DiscardOutcome.Rejected rejected => Problem(rejected.Code, rejected.Message),
            _ => Problem(RefusalCode.InternalError, "Unknown discard outcome."),
        };
    }

    private static DiscardSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}
