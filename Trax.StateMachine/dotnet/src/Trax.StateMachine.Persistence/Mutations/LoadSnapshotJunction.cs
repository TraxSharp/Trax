using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Resume read: load the caller's stored draft. A missing draft is normal (start fresh), not an error. Infrastructure chained by <see cref="LoadSnapshot"/>; not intended to be called directly.</summary>
internal sealed class LoadSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<LoadSnapshotJunction>? logger = null,
    IServiceProvider? services = null
) : Junction<LoadSnapshotInput, LoadSnapshotOutput>
{
    /// <summary>
    /// Loads the current user's draft, as a client held to the machine's <c>get</c> rule. A missing (or expired)
    /// draft, and one the rule does not let the caller read, come back alike as the <c>NOT_FOUND</c> problem, which
    /// clients treat as "start fresh"; a stored draft that fails validation comes back with its rehydration error
    /// code.
    /// </summary>
    public override async Task<LoadSnapshotOutput> Run(LoadSnapshotInput input)
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
            .Load(
                CallerRules.Client(userKey, CallerRules.Caller(services)),
                input.Id,
                CancellationToken
            )
            .ConfigureAwait(false) switch
        {
            LoadResult.Loaded loaded => new LoadSnapshotOutput
            {
                Snapshot = service.Serialize(loaded.Snapshot),
                Revision = loaded.Revision,
                RowId = loaded.RowId,
                DueAt = loaded.DueAt,
            },
            LoadResult.NotFound => Problem(RefusalCode.NotFound, "No draft to resume."),
            LoadResult.Invalid invalid => new LoadSnapshotOutput
            {
                Problem = SnapshotGuards.Failure(
                    logger,
                    invalid.Code,
                    invalid.Message,
                    invalid.Exception,
                    input.Machine,
                    input.Id
                ),
            },
            _ => Problem(RefusalCode.InternalError, "Unknown load result."),
        };
    }

    private static LoadSnapshotOutput Problem(RefusalCode code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}
