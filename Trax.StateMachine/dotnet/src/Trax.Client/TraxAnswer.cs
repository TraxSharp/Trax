using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>One error of a GraphQL answer.</summary>
/// <param name="Message">What the host said.</param>
/// <param name="Code">Its <c>extensions.code</c> (<c>CLIENT_OUTDATED</c>, <c>AUTH_NOT_AUTHORIZED</c>), when it has one.</param>
/// <param name="Path">The path of the field it belongs to, when it belongs to one.</param>
/// <param name="Extensions">Its <c>extensions</c> as sent.</param>
public sealed record TraxGraphQLError(
    string Message,
    string? Code,
    JsonArray? Path,
    JsonObject? Extensions
)
{
    internal static IReadOnlyList<TraxGraphQLError> ListOf(JsonNode? errors) =>
        errors is JsonArray array ? array.Select(OfNode).ToList() : [];

    private static TraxGraphQLError OfNode(JsonNode? node)
    {
        var error = node as JsonObject;
        var extensions = error?["extensions"] as JsonObject;
        return new TraxGraphQLError(
            Text(error?["message"]) ?? "The host answered with an error.",
            Text(extensions?["code"]),
            error?["path"]?.DeepClone() as JsonArray,
            extensions?.DeepClone() as JsonObject
        );
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>A request that got no GraphQL answer: the host could not be reached, or answered with something else.</summary>
/// <param name="Message">What went wrong.</param>
/// <param name="Status">The HTTP status, when a response arrived.</param>
public sealed record TraxTransportFailure(string Message, int? Status = null)
{
    /// <summary>The code a transport failure is reported under, as the state-machine clients report it.</summary>
    public const string Code = "TRANSPORT_ERROR";

    /// <summary>
    /// The close code the host closed the socket with when it turned a stream away for good (4401 unauthorized, 4403
    /// forbidden); null for any other failure.
    /// </summary>
    public int? CloseStatus { get; init; }
}

/// <summary>
/// A GraphQL answer as it arrived: its <c>data</c> and <c>errors</c>, or the <see cref="Failure"/> that kept it from
/// arriving. What an <see cref="ITraxTransport"/> returns; <see cref="TraxClient"/> reads it into a
/// <see cref="TraxAnswer{T}"/>.
/// </summary>
public sealed record TraxAnswer
{
    /// <summary>The answer's <c>data</c>, or null.</summary>
    public JsonNode? Data { get; init; }

    /// <summary>The answer's <c>errors</c>; empty when it had none.</summary>
    public IReadOnlyList<TraxGraphQLError> Errors { get; init; } = [];

    /// <summary>Why no answer arrived; null when one did.</summary>
    public TraxTransportFailure? Failure { get; init; }

    /// <summary>An answer that never arrived.</summary>
    public static TraxAnswer Failed(string message, int? status = null) =>
        new() { Failure = new TraxTransportFailure(message, status) };

    /// <summary>Reads an answer's JSON: null when it holds neither <c>data</c> nor <c>errors</c>.</summary>
    public static TraxAnswer? Of(JsonNode? answer) =>
        answer is JsonObject body && (body.ContainsKey("data") || body.ContainsKey("errors"))
            ? new TraxAnswer
            {
                Data = body["data"]?.DeepClone(),
                Errors = TraxGraphQLError.ListOf(body["errors"]),
            }
            : null;
}

/// <summary>
/// A document's answer: its <see cref="Data"/>, the <see cref="Errors"/> the host answered with, or the
/// <see cref="Failure"/> that kept an answer from arriving. A refusal is an answer, never an exception.
/// </summary>
public sealed record TraxAnswer<T>
{
    /// <summary>The data, read into the document's result; null when the host answered none.</summary>
    public T? Data { get; init; }

    /// <summary>The errors the host answered with; empty when it had none.</summary>
    public IReadOnlyList<TraxGraphQLError> Errors { get; init; } = [];

    /// <summary>Why no answer arrived; null when one did.</summary>
    public TraxTransportFailure? Failure { get; init; }

    /// <summary>Whether data arrived with no error.</summary>
    public bool Succeeded => Failure is null && Errors.Count == 0 && Data is not null;

    /// <summary>
    /// The code to act on: <see cref="TraxTransportFailure.Code"/> for a failure, else the first error's code; null
    /// when the request succeeded or the error carries none.
    /// </summary>
    public string? Code =>
        Failure is not null ? TraxTransportFailure.Code : Errors.FirstOrDefault()?.Code;

    /// <summary>One line to show: the failure, every error's message, or null when it succeeded.</summary>
    public string? Message =>
        Failure?.Message
        ?? (
            Errors.Count > 0
                ? string.Join(Environment.NewLine, Errors.Select(e => e.Message))
                : null
        );
}
