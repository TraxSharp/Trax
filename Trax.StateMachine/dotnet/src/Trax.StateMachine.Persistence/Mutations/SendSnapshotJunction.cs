using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Run a machine's one irreversible effect exactly once (state-gated, idempotent). Infrastructure chained by <see cref="SendSnapshot"/>; not intended to be called directly.</summary>
internal sealed class SendSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<SendSnapshotJunction>? logger = null,
    IServiceProvider? services = null
) : Junction<SendSnapshotInput, SendSnapshotOutput>
{
    /// <summary>
    /// Runs the machine's irreversible effect for the current user's draft, keyed by
    /// <see cref="SendSnapshotInput.RequestId"/> or <c>send:{Id}</c> when none is given. Refusals are returned
    /// as a <see cref="SnapshotProblem"/>, including <c>NO_EFFECT</c> when the machine declares none. Any other
    /// exception is caught and returned as <c>DELIVERY_FAILED</c> with a fixed message and a reference, and the
    /// exception is logged under that reference; the draft is not advanced. A cancelled request is not a failed
    /// delivery, so an <see cref="OperationCanceledException"/> propagates when this request was cancelled; one
    /// raised by the effect itself, such as an outbound call's timeout, is a <c>DELIVERY_FAILED</c>.
    /// </summary>
    public override async Task<SendSnapshotOutput> Run(SendSnapshotInput input)
    {
        if (SnapshotOwner.Key(principal) is not { } userKey)
            return Problem(
                RefusalCode.Unauthenticated,
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is null)
            return Problem(
                RefusalCode.UnknownMachine,
                $"No registered machine named '{input.Machine}'."
            );
        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);
        if (registry.EffectRunner(input.Machine) is not { } runner)
            return Problem(
                RefusalCode.NoEffect,
                $"Machine '{input.Machine}' has no irreversible effect to send."
            );

        // Absent a client key, one derived from the draft id is the idempotency key, so a bare double-send
        // replays. It is prefixed because advance and send share one request-id namespace: a client that used
        // the bare draft id as an advance's key must not have its Send refused as a reused id.
        // A blank one is the client's and names no request: it is refused, as on every other event path.
        var requestId = input.RequestId ?? $"send:{input.Id}";

        var registryService = registry.Service(input.Machine)!;
        try
        {
            // The effect-bound event's rule is checked against the request's user before the effect is claimed.
            var sent = runner is IActorEffectRunner actorRunner
                ? await actorRunner
                    .Run(
                        CallerRules.Client(userKey, CallerRules.Caller(services)),
                        input.Id,
                        requestId,
                        CancellationToken
                    )
                    .ConfigureAwait(false)
                : await runner
                    .Run(userKey, input.Id, requestId, CancellationToken)
                    .ConfigureAwait(false);
            return sent switch
            {
                AdvanceOutcome.Advanced advanced => new SendSnapshotOutput
                {
                    Snapshot = registryService.Serialize(advanced.Snapshot),
                    Revision = advanced.Revision,
                    RowId = advanced.RowId,
                    DueAt = advanced.DueAt,
                    DueAtKnown = advanced.DueAtKnown,
                },
                AdvanceOutcome.Rejected rejected => Failed(
                    SnapshotGuards.Failure(
                        logger,
                        rejected.Reason,
                        rejected.Detail ?? rejected.Reason.ToWire(),
                        rejected.Exception,
                        input.Machine,
                        input.Id
                    )
                ),
                AdvanceOutcome.NotFound => Problem(RefusalCode.NotFound, "No draft with that id."),
                AdvanceOutcome.LoadError loadError => Failed(
                    SnapshotGuards.Failure(
                        logger,
                        loadError.Code,
                        loadError.Message,
                        loadError.Exception,
                        input.Machine,
                        input.Id
                    )
                ),
                AdvanceOutcome.Conflict => Problem(
                    RefusalCode.Conflict,
                    "The draft changed elsewhere; reload and retry."
                ),
                _ => Problem(RefusalCode.InternalError, "Unknown send outcome."),
            };
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !CancellationToken.IsCancellationRequested
            )
        {
            // The effect (or the store around it) threw: the draft was NOT advanced, so the user can retry.
            // Surfaced as data with a fixed message: the exception's own text can carry anything, a connection
            // string included, so it goes to the log under the reference the client is given.
            return Failed(
                SnapshotGuards.Failure(
                    logger,
                    RefusalCode.DeliveryFailed,
                    "The action could not be completed. Try again.",
                    ex,
                    input.Machine,
                    input.Id
                )
            );
        }
    }

    private static SendSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };

    private static SendSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}
