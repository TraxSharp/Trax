using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Start a new instance from the machine's declared start, under a server-minted id. Infrastructure chained by <see cref="StartSnapshot"/>; not intended to be called directly.</summary>
internal sealed class StartSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    IServiceProvider? services = null
) : Junction<StartSnapshotInput, StartSnapshotOutput>
{
    /// <summary>
    /// Starts an instance for the current user. Refusals are returned as a <see cref="SnapshotProblem"/>:
    /// <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, <c>SCHEMA_MISMATCH</c>, <c>TOO_LARGE</c> (input over
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/>), <c>MALFORMED</c> (input is not a JSON object), or the code of a
    /// <see cref="StartOutcome.Rejected"/>.
    /// </summary>
    public override async Task<StartSnapshotOutput> Run(StartSnapshotInput input)
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

        if (
            SnapshotGuards.Bytes(input.Input) + SnapshotGuards.Bytes(input.SensitiveInput)
            > SnapshotLimits.MaxSnapshotBytes
        )
            return Problem(
                RefusalCode.TooLarge,
                $"The start input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        JsonObject? startInput;
        try
        {
            startInput = SnapshotGuards.Merge(input.Input, input.SensitiveInput) switch
            {
                null => null,
                JsonObject obj => obj,
                _ => throw new JsonException("The start input is not a JSON object."),
            };
        }
        catch (JsonException)
        {
            return Problem(RefusalCode.Malformed, "The start input is not a JSON object.");
        }

        // The request's user acts as a client, checked against the start's rule with the roles and policies it
        // holds now.
        var outcome = await service
            .Start(
                CallerRules.Client(userKey, CallerRules.Caller(services)),
                startInput,
                input.RequestId,
                CancellationToken
            )
            .ConfigureAwait(false);

        return outcome switch
        {
            StartOutcome.Started started => new StartSnapshotOutput
            {
                Id = started.Id,
                Created = started.Created,
                Snapshot = service.Serialize(started.Snapshot),
                Revision = started.Revision,
                DueAt = started.DueAt,
            },
            StartOutcome.Rejected rejected => Problem(rejected.Code, rejected.Message),
            _ => Problem(RefusalCode.InternalError, "Unknown start outcome."),
        };
    }

    private static StartSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}
