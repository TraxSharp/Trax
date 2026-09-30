using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// A refusal to return to the client. When it came from an exception (a guard, reducer or migration that
    /// threw, or an effect that failed), the exception's text stays on the server: it is logged under a fresh
    /// reference, and the client gets the fixed <paramref name="message"/> with that reference, so an operator
    /// can find the log entry without the response carrying whatever the exception said.
    /// </summary>
    public static SnapshotProblem Failure(
        ILogger? logger,
        string code,
        string message,
        Exception? exception,
        string machine,
        Guid id
    )
    {
        if (exception is null)
            return new SnapshotProblem { Code = code, Message = message };

        var reference = Guid.NewGuid().ToString("N");
        logger?.LogError(
            exception,
            "The {Machine} draft {DraftId} failed with {Code}. Reference {Reference}.",
            machine,
            id,
            code,
            reference
        );
        return new SnapshotProblem { Code = code, Message = $"{message} Reference: {reference}." };
    }
}

/// <summary>Autosave (soft path): validate + store a client snapshot for any registered machine. Infrastructure chained by <see cref="SaveSnapshot"/>; not intended to be called directly.</summary>
internal class SaveSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<SaveSnapshotJunction>? logger = null
) : Junction<SaveSnapshotInput, SaveSnapshotOutput>
{
    /// <summary>
    /// Autosaves <see cref="SaveSnapshotInput.Snapshot"/> for the current user. Every refusal is returned as a
    /// <see cref="SnapshotProblem"/>: <c>unauthenticated</c>, <c>unknown-machine</c>, <c>schema-mismatch</c>,
    /// <c>conflict</c>, or the code of an <see cref="AutosaveResult.Rejected"/>.
    /// </summary>
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

    private static SaveSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}

/// <summary>Authoritative advance: re-drive the stored draft by one trigger, server-side. Infrastructure chained by <see cref="AdvanceSnapshot"/>; not intended to be called directly.</summary>
internal class AdvanceSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<AdvanceSnapshotJunction>? logger = null
) : Junction<AdvanceSnapshotInput, AdvanceSnapshotOutput>
{
    /// <summary>
    /// Advances the current user's draft by one trigger. Refusals are returned as a
    /// <see cref="SnapshotProblem"/>: <c>unauthenticated</c>, <c>unknown-machine</c>, <c>schema-mismatch</c>,
    /// <c>too-large</c> (input over <see cref="SnapshotLimits.MaxSnapshotBytes"/>), <c>malformed</c> (input
    /// is not JSON), <c>not-found</c>, <c>conflict</c>, or the reason of an <see cref="AdvanceOutcome.Rejected"/>.
    /// </summary>
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

        // The same bound autosave applies, checked before parsing: a reducer that copies its input into the
        // context would otherwise store whatever the request body carried.
        if (
            input.Input is { } raw
            && Encoding.UTF8.GetByteCount(raw) > SnapshotLimits.MaxSnapshotBytes
        )
            return Problem(
                "too-large",
                $"The trigger input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        JsonNode? triggerInput;
        try
        {
            triggerInput = string.IsNullOrEmpty(input.Input) ? null : JsonNode.Parse(input.Input);
        }
        catch (JsonException)
        {
            return Problem("malformed", "The trigger input is not valid JSON.");
        }

        // Runtime differential: when the client sent the snapshot its twin computed for this advance, the
        // service compares it with its own result before writing and refuses a divergence (client-divergence)
        // with nothing persisted, so the client reloads the draft as it was.
        var outcome = await service.Advance(
            userKey,
            input.Id,
            input.Trigger,
            triggerInput,
            input.RequestId,
            input.ClientResult,
            CancellationToken
        );

        return outcome switch
        {
            AdvanceOutcome.Advanced advanced => new AdvanceSnapshotOutput
            {
                Snapshot = service.Serialize(advanced.Snapshot),
            },
            AdvanceOutcome.Rejected rejected => Failed(
                SnapshotGuards.Failure(
                    logger,
                    rejected.Reason,
                    rejected.Detail ?? rejected.Reason,
                    rejected.Exception,
                    input.Machine,
                    input.Id
                )
            ),
            AdvanceOutcome.NotFound => Problem("not-found", "No draft with that id."),
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

    private static AdvanceSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}

/// <summary>Resume read: load the caller's stored draft. A missing draft is normal (start fresh), not an error. Infrastructure chained by <see cref="LoadSnapshot"/>; not intended to be called directly.</summary>
internal class LoadSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<LoadSnapshotJunction>? logger = null
) : Junction<LoadSnapshotInput, LoadSnapshotOutput>
{
    /// <summary>
    /// Loads the current user's draft. A missing (or expired) draft comes back as the <c>not-found</c>
    /// problem, which clients treat as "start fresh"; a stored draft that fails validation comes back with
    /// its rehydration error code.
    /// </summary>
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
            _ => Problem("internal-error", "Unknown load result."),
        };
    }

    private static LoadSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };
}

/// <summary>Run a machine's one irreversible effect exactly once (state-gated, idempotent). Infrastructure chained by <see cref="SendSnapshot"/>; not intended to be called directly.</summary>
internal class SendSnapshotJunction(
    ISnapshotMachineRegistry registry,
    ISnapshotPrincipal principal,
    ILogger<SendSnapshotJunction>? logger = null
) : Junction<SendSnapshotInput, SendSnapshotOutput>
{
    /// <summary>
    /// Runs the machine's irreversible effect for the current user's draft, keyed by
    /// <see cref="SendSnapshotInput.RequestId"/> or <c>send:{Id}</c> when none is given. Refusals are returned
    /// as a <see cref="SnapshotProblem"/>, including <c>no-effect</c> when the machine declares none. Any other
    /// exception is caught and returned as <c>delivery-failed</c> with a fixed message and a reference, and the
    /// exception is logged under that reference; the draft is not advanced. A cancelled request is not a failed
    /// delivery, so an <see cref="OperationCanceledException"/> propagates.
    /// </summary>
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

        // Absent a client key, one derived from the draft id is the idempotency key, so a bare double-send
        // replays. It is prefixed because advance and send share one request-id namespace: a client that used
        // the bare draft id as an advance's key must not have its Send refused as a reused id.
        var requestId = string.IsNullOrEmpty(input.RequestId)
            ? $"send:{input.Id}"
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
                AdvanceOutcome.Rejected rejected => Failed(
                    SnapshotGuards.Failure(
                        logger,
                        rejected.Reason,
                        rejected.Detail ?? rejected.Reason,
                        rejected.Exception,
                        input.Machine,
                        input.Id
                    )
                ),
                AdvanceOutcome.NotFound => Problem("not-found", "No draft with that id."),
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
                    "conflict",
                    "The draft changed elsewhere; reload and retry."
                ),
                _ => Problem("internal-error", "Unknown send outcome."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The effect (or the store around it) threw: the draft was NOT advanced, so the user can retry.
            // Surfaced as data with a fixed message: the exception's own text can carry anything, a connection
            // string included, so it goes to the log under the reference the client is given.
            return Failed(
                SnapshotGuards.Failure(
                    logger,
                    "delivery-failed",
                    "The action could not be completed. Try again.",
                    ex,
                    input.Machine,
                    input.Id
                )
            );
        }
    }

    private static SendSnapshotOutput Problem(string code, string message) =>
        new()
        {
            Problem = new SnapshotProblem { Code = code, Message = message },
        };

    private static SendSnapshotOutput Failed(SnapshotProblem problem) =>
        new() { Problem = problem };
}
