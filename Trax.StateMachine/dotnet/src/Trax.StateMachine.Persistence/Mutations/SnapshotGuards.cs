using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>Cross-cutting runtime guards shared by the snapshot mutations.</summary>
internal static class SnapshotGuards
{
    /// <summary>The refusal code of a client prediction that differs from the server's result.</summary>
    public const RefusalCode ClientDivergence = RefusalCode.ClientDivergence;

    /// <summary>The UTF-8 length of <paramref name="value"/>, or 0 for null.</summary>
    public static int Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    /// <summary>
    /// An input as JSON, with the fields of <paramref name="sensitive"/> (a JSON object) added to it. Null when both
    /// are empty. Throws <see cref="JsonException"/> when either is not valid JSON, when the sensitive part is not an
    /// object, when the input it joins is not an object, or when one field is in both.
    /// </summary>
    public static JsonNode? Merge(string? input, string? sensitive)
    {
        var parsed = string.IsNullOrEmpty(input) ? null : JsonNode.Parse(input);
        if (string.IsNullOrEmpty(sensitive))
            return parsed;
        if (JsonNode.Parse(sensitive) is not JsonObject extra)
            throw new JsonException("The sensitive input is not a JSON object.");
        var merged = parsed switch
        {
            null => new JsonObject(),
            JsonObject obj => obj,
            _ => throw new JsonException("The input a sensitive input joins is not a JSON object."),
        };
        foreach (var (name, value) in extra.ToList())
        {
            if (merged.ContainsKey(name))
                throw new JsonException($"The input names '{name}' twice.");
            extra.Remove(name);
            merged[name] = value;
        }
        return merged;
    }

    /// <summary>
    /// The runtime schema-hash handshake. When the client sends its machine hash (<see cref="IMachine.SchemaHash"/>,
    /// embedded in the twin) and it differs from the server's registered machine, the client is running an
    /// outdated definition. Returns a <c>SCHEMA_MISMATCH</c> problem so the request is refused and the client
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
                Code = RefusalCode.SchemaMismatch,
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
        RefusalCode code,
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
            code.ToWire(),
            reference
        );
        return new SnapshotProblem { Code = code, Message = $"{message} Reference: {reference}." };
    }
}
