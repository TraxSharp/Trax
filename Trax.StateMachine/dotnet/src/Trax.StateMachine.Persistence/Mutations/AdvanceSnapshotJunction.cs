using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Authoritative advance: re-drive the stored draft by one trigger, server-side. Infrastructure chained by <see cref="AdvanceSnapshot"/>; not intended to be called directly.</summary>
internal sealed class AdvanceSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<AdvanceSnapshotJunction>? logger = null,
    IServiceProvider? services = null
) : Junction<AdvanceSnapshotInput, AdvanceSnapshotOutput>
{
    /// <summary>
    /// Advances the current user's draft by one trigger. Refusals are returned as a
    /// <see cref="SnapshotProblem"/>: <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, <c>SCHEMA_MISMATCH</c>,
    /// <c>TOO_LARGE</c> (input over <see cref="SnapshotLimits.MaxSnapshotBytes"/>), <c>MALFORMED</c> (input
    /// is not JSON), <c>NOT_FOUND</c>, <c>CONFLICT</c>, or the reason of an <see cref="AdvanceOutcome.Rejected"/>.
    /// </summary>
    public override async Task<AdvanceSnapshotOutput> Run(AdvanceSnapshotInput input)
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

        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);

        if (Oversized(input) is { } oversized)
            return oversized;

        JsonNode? triggerInput;
        try
        {
            triggerInput = SnapshotGuards.Merge(input.Input, input.SensitiveInput);
        }
        catch (JsonException)
        {
            return Problem(RefusalCode.Malformed, "The trigger input is not valid JSON.");
        }

        // Runtime differential: when the client sent the snapshot its twin computed for this advance, the
        // service compares it with its own result before writing and refuses a divergence (client-divergence)
        // with nothing persisted, so the client reloads the draft as it was. The request's user acts as a client,
        // checked against the event's rule with the roles and policies it holds now.
        var outcome = await service
            .Advance(
                CallerRules.Client(userKey, CallerRules.Caller(services)),
                input.Id,
                input.Trigger,
                triggerInput,
                input.RequestId,
                input.ClientResult,
                CancellationToken
            )
            .ConfigureAwait(false);

        LogDivergence(input, outcome);

        return Answer(outcome, service, input);
    }

    private static AdvanceSnapshotOutput? Oversized(AdvanceSnapshotInput input)
    {
        // The same bound autosave applies, checked before parsing: a reducer that copies its input into the
        // context would otherwise store whatever the request body carried.
        if (
            SnapshotGuards.Bytes(input.Input) + SnapshotGuards.Bytes(input.SensitiveInput)
            > SnapshotLimits.MaxSnapshotBytes
        )
            return Problem(
                RefusalCode.TooLarge,
                $"The trigger input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        // A client's prediction is a snapshot, bounded like one, and refused before it is compared.
        if (SnapshotGuards.Bytes(input.ClientResult) > SnapshotLimits.MaxSnapshotBytes)
            return Problem(
                RefusalCode.TooLarge,
                $"The client result exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );
        return null;
    }

    private void LogDivergence(AdvanceSnapshotInput input, AdvanceOutcome outcome)
    {
        // Twin drift is worth an operator's attention, but the client's prediction can carry anything the context
        // holds: only its length and hash are logged.
        if (
            outcome is AdvanceOutcome.Rejected { Reason: SnapshotGuards.ClientDivergence }
            && input.ClientResult is { } predicted
        )
            logger?.LogWarning(
                "The {Machine} draft {DraftId} was refused: the client's result for {Trigger} diverged from the "
                    + "server's ({Length} characters, SHA-256 {Hash}).",
                input.Machine,
                input.Id,
                input.Trigger,
                predicted.Length,
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(predicted))
                )
            );
    }

    private AdvanceSnapshotOutput Answer(
        AdvanceOutcome outcome,
        ISnapshotDraftService service,
        AdvanceSnapshotInput input
    ) =>
        outcome switch
        {
            AdvanceOutcome.Advanced advanced => new AdvanceSnapshotOutput
            {
                Snapshot = service.Serialize(advanced.Snapshot),
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
            _ => Problem(RefusalCode.InternalError, "Unknown advance outcome."),
        };

    private static AdvanceSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };

    private static AdvanceSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}
