using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Autosave (soft path): validate + store a client snapshot for any registered machine. Infrastructure chained by <see cref="SaveSnapshot"/>; not intended to be called directly.</summary>
internal sealed class SaveSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<SaveSnapshotJunction>? logger = null
) : Junction<SaveSnapshotInput, SaveSnapshotOutput>
{
    /// <summary>
    /// Autosaves <see cref="SaveSnapshotInput.Snapshot"/> for the current user. Every refusal is returned as a
    /// <see cref="SnapshotProblem"/>: <c>UNAUTHENTICATED</c>, <c>UNKNOWN_MACHINE</c>, <c>SCHEMA_MISMATCH</c>,
    /// <c>CONFLICT</c>, or the code of an <see cref="AutosaveResult.Rejected"/>.
    /// </summary>
    public override async Task<SaveSnapshotOutput> Run(SaveSnapshotInput input)
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

        return await service
            .Autosave(userKey, input.Id, input.Snapshot, CancellationToken)
            .ConfigureAwait(false) switch
        {
            AutosaveResult.Saved saved => new SaveSnapshotOutput
            {
                Snapshot = service.Serialize(saved.Snapshot),
                Revision = saved.Revision,
                RowId = saved.RowId,
                DueAt = saved.DueAt,
            },
            AutosaveResult.Rejected rejected => Failed(
                SnapshotGuards.Failure(
                    logger,
                    rejected.Code,
                    rejected.Message,
                    rejected.Exception,
                    input.Machine,
                    input.Id
                )
            ),
            AutosaveResult.Conflict => Problem(
                RefusalCode.Conflict,
                "The draft changed elsewhere; reload and retry."
            ),
            _ => Problem(RefusalCode.InternalError, "Unknown autosave result."),
        };
    }

    private static SaveSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };

    private static SaveSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}
