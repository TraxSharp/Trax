using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Junction;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

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

        JsonNode? triggerInput;
        try
        {
            triggerInput = string.IsNullOrEmpty(input.Input) ? null : JsonNode.Parse(input.Input);
        }
        catch (JsonException)
        {
            return Problem("malformed", "The trigger input is not valid JSON.");
        }

        return await service.Advance(
            userKey,
            input.Id,
            input.Trigger,
            triggerInput,
            input.RequestId,
            CancellationToken
        ) switch
        {
            AdvanceOutcome.Advanced advanced => new AdvanceSnapshotOutput
            {
                Snapshot = service.Serialize(advanced.Snapshot),
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
