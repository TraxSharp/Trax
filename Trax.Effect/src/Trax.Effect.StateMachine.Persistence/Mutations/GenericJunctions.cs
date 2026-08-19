using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Junction;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

/// <summary>Cross-cutting runtime guards shared by the snapshot mutations.</summary>
internal static class SnapshotGuards
{
    /// <summary>
    /// The runtime schema-hash handshake. When the client sends its machine hash (<see cref="IMachine.SchemaHash"/>,
    /// embedded in the twin) and it differs from the server's registered machine, the client is running an
    /// outdated definition. Returns a <c>schema-mismatch</c> problem so the request is refused and the client
    /// reloads; null when the hashes agree or the client sent none (an older client — no check, so the handshake
    /// rolls out gradually as clients start sending their hash).
    /// </summary>
    public static SnapshotProblem? SchemaMismatch(
        ISnapshotMachineRegistry registry,
        string machine,
        string? clientHash
    ) =>
        clientHash is { } client
        && registry.SchemaHash(machine) is { } server
        && !string.Equals(client, server, StringComparison.Ordinal)
            ? new SnapshotProblem
            {
                Code = "schema-mismatch",
                Message =
                    "This client is running an outdated version of the machine. Reload the page to continue.",
            }
            : null;
}

/// <summary>Autosave (soft path): validate + store a client snapshot for any registered machine.</summary>
public class SaveSnapshotJunction(ISnapshotMachineRegistry registry, ISnapshotPrincipal principal)
    : Junction<SaveSnapshotInput, SaveSnapshotOutput>
{
    public override async Task<SaveSnapshotOutput> Run(SaveSnapshotInput input)
    {
        if (principal.CurrentUserKey is not { } userKey)
            return Problem(
                "unauthenticated",
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is not { } service)
            return Problem("unknown-machine", $"No registered machine named '{input.Machine}'.");

        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);

        return await service.Autosave(userKey, input.Id, input.Snapshot, CancellationToken) switch
        {
            AutosaveResult.Saved saved => new SaveSnapshotOutput
            {
                Snapshot = service.Serialize(saved.Snapshot),
            },
            AutosaveResult.Rejected rejected => Problem(rejected.Code, rejected.Message),
            AutosaveResult.Conflict => Problem(
                "conflict",
                "The draft changed elsewhere; reload and retry."
            ),
            _ => Problem("internal-error", "Unknown autosave result."),
        };
    }

    private static SaveSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}

/// <summary>Authoritative advance: re-drive the stored draft by one trigger, server-side.</summary>
public class AdvanceSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal
) : Junction<AdvanceSnapshotInput, AdvanceSnapshotOutput>
{
    public override async Task<AdvanceSnapshotOutput> Run(AdvanceSnapshotInput input)
    {
        if (principal.CurrentUserKey is not { } userKey)
            return Problem(
                "unauthenticated",
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is not { } service)
            return Problem("unknown-machine", $"No registered machine named '{input.Machine}'.");

        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);

        JsonNode? triggerInput;
        try
        {
            triggerInput = string.IsNullOrEmpty(input.Input) ? null : JsonNode.Parse(input.Input);
        }
        catch (JsonException)
        {
            return Problem("malformed", "The trigger input is not valid JSON.");
        }

        var outcome = await service.Advance(
            userKey,
            input.Id,
            input.Trigger,
            triggerInput,
            input.RequestId,
            CancellationToken
        );

        if (outcome is AdvanceOutcome.Advanced advanced)
        {
            var serverWire = service.Serialize(advanced.Snapshot);
            // Runtime differential: if the client sent the snapshot its twin computed for this advance, it must
            // equal the server's authoritative result (both are canonical wire). A divergence means the two
            // engines disagreed on a real transition — a skew the schema hash missed, or a genuine bug. Enforce:
            // refuse and let the client reload. The server's result is authoritative regardless.
            if (
                input.ClientResult is { } clientWire
                && !string.Equals(clientWire, serverWire, StringComparison.Ordinal)
            )
                return Problem(
                    "client-divergence",
                    "The client and server disagree on this transition; reload to continue."
                );
            return new AdvanceSnapshotOutput { Snapshot = serverWire };
        }

        return outcome switch
        {
            AdvanceOutcome.Rejected rejected => Problem(
                rejected.Reason,
                rejected.Detail ?? rejected.Reason
            ),
            AdvanceOutcome.NotFound => Problem("not-found", "No draft with that id."),
            AdvanceOutcome.LoadError loadError => Problem(loadError.Code, loadError.Message),
            AdvanceOutcome.Conflict => Problem(
                "conflict",
                "The draft changed elsewhere; reload and retry."
            ),
            _ => Problem("internal-error", "Unknown advance outcome."),
        };
    }

    private static AdvanceSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}

/// <summary>Resume read: load the caller's stored draft. A missing draft is normal (start fresh), not an error.</summary>
public class LoadSnapshotJunction(ISnapshotMachineRegistry registry, ISnapshotPrincipal principal)
    : Junction<LoadSnapshotInput, LoadSnapshotOutput>
{
    public override async Task<LoadSnapshotOutput> Run(LoadSnapshotInput input)
    {
        if (principal.CurrentUserKey is not { } userKey)
            return Problem(
                "unauthenticated",
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is not { } service)
            return Problem("unknown-machine", $"No registered machine named '{input.Machine}'.");

        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);

        return await service.Load(userKey, input.Id, CancellationToken) switch
        {
            LoadResult.Loaded loaded => new LoadSnapshotOutput
            {
                Snapshot = service.Serialize(loaded.Snapshot),
            },
            LoadResult.NotFound => Problem("not-found", "No draft to resume."),
            LoadResult.Invalid invalid => Problem(invalid.Code, invalid.Message),
            _ => Problem("internal-error", "Unknown load result."),
        };
    }

    private static LoadSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}

/// <summary>Run a machine's one irreversible effect exactly once (state-gated, idempotent).</summary>
public class SendSnapshotJunction(ISnapshotMachineRegistry registry, ISnapshotPrincipal principal)
    : Junction<SendSnapshotInput, SendSnapshotOutput>
{
    public override async Task<SendSnapshotOutput> Run(SendSnapshotInput input)
    {
        if (principal.CurrentUserKey is not { } userKey)
            return Problem(
                "unauthenticated",
                "No authenticated user is associated with this request."
            );
        if (registry.Service(input.Machine) is null)
            return Problem("unknown-machine", $"No registered machine named '{input.Machine}'.");
        if (
            SnapshotGuards.SchemaMismatch(registry, input.Machine, input.SchemaHash) is { } mismatch
        )
            return Problem(mismatch.Code, mismatch.Message);
        if (registry.EffectRunner(input.Machine) is not { } runner)
            return Problem(
                "no-effect",
                $"Machine '{input.Machine}' has no irreversible effect to send."
            );

        // Absent a client key, the draft id IS the idempotency key, so a bare double-send replays.
        var requestId = string.IsNullOrEmpty(input.RequestId)
            ? input.Id.ToString()
            : input.RequestId;

        var registryService = registry.Service(input.Machine)!;
        try
        {
            return await runner.Run(userKey, input.Id, requestId, CancellationToken) switch
            {
                AdvanceOutcome.Advanced advanced => new SendSnapshotOutput
                {
                    Snapshot = registryService.Serialize(advanced.Snapshot),
                },
                AdvanceOutcome.Rejected rejected => Problem(
                    rejected.Reason,
                    rejected.Detail ?? rejected.Reason
                ),
                AdvanceOutcome.NotFound => Problem("not-found", "No draft with that id."),
                AdvanceOutcome.LoadError loadError => Problem(loadError.Code, loadError.Message),
                AdvanceOutcome.Conflict => Problem(
                    "conflict",
                    "The draft changed elsewhere; reload and retry."
                ),
                _ => Problem("internal-error", "Unknown send outcome."),
            };
        }
        catch (Exception ex)
        {
            // The effect threw: the draft was NOT advanced, so the user can retry. Surfaced as data.
            return Problem("delivery-failed", ex.Message);
        }
    }

    private static SendSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}
