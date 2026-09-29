using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution;
using Trax.Api.Exceptions;
using Trax.Core.Exceptions;
using Trax.Mediator.Exceptions;

namespace Trax.Api.GraphQL.Errors;

/// <summary>
/// Replaces HotChocolate's default <c>"Unexpected Execution Error"</c> with a
/// curated message for train-related exceptions. Unknown exception types retain
/// the default masked message so internal detail never leaks to clients.
/// </summary>
/// <remarks>
/// Exposed exception types and their public semantics:
/// <list type="bullet">
/// <item>
/// <see cref="TrainAuthorizationException"/>: public message is always
/// <c>"Not authorized."</c>; code <c>TRAX_AUTHORIZATION</c>. The train name,
/// policy name, and role name are intentionally omitted so unauthenticated clients
/// cannot enumerate the protected surface.
/// </item>
/// <item>
/// <see cref="TrainNotFoundException"/>: public message is always
/// <c>"The requested train was not found."</c>; code <c>TRAX_TRAIN_NOT_FOUND</c>.
/// </item>
/// <item>
/// <see cref="AmbiguousTrainNameException"/>: public message explains how to
/// disambiguate; code <c>TRAX_AMBIGUOUS_TRAIN</c>. The candidate FullNames are
/// included in the message because resolving the ambiguity requires them.
/// </item>
/// <item>
/// <see cref="TrainInputValidationException"/>: public message is always the
/// generic <c>"The train input failed validation."</c>; code <c>TRAX_INVALID_INPUT</c>.
/// The cap and observed size are intentionally not echoed to the client.
/// </item>
/// <item>
/// <see cref="TrainException"/>: execution failures; code <c>TRAX_TRAIN_ERROR</c>. A
/// message a train author wrote is passed through, and authors are expected to treat it as
/// client-safe. A message that carries another exception, as the structured
/// <see cref="TrainExceptionData"/> JSON a remote worker or a nested train produces, passes
/// through only the carried message of a carried <see cref="TrainException"/>; any other
/// carried type, and the scheduler's remote-transport failures, get
/// <see cref="TrainFailedMessage"/>. The detail stays in the recorded metadata and the logs.
/// See <c>docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md</c>.
/// </item>
/// </list>
/// Any other exception type (including <see cref="InvalidOperationException"/>) is
/// left with HotChocolate's default masked message. Use the typed exceptions above
/// when you want a message to reach the client.
/// </remarks>
internal class TraxErrorFilter : IErrorFilter
{
    /// <summary>
    /// What a client is told when a train failed with something other than a TrainException's
    /// own message.
    /// </summary>
    internal const string TrainFailedMessage = "The train failed.";

    public IError OnError(IError error)
    {
        // HotChocolate's @authorize directive (used by [TraxAuthorize] on
        // [TraxQueryModel] entities) raises errors without an attached
        // exception — they carry only a code. Normalise both authentication-
        // and authorization-failure codes to the Trax public shape so callers
        // see a single uniform error regardless of which path failed.
        if (error.Code is "AUTH_NOT_AUTHENTICATED" or "AUTH_NOT_AUTHORIZED")
            return error
                .WithMessage(TrainAuthorizationException.PublicMessage)
                .WithCode("TRAX_AUTHORIZATION");

        if (error.Exception is null)
            return error;

        return error.Exception switch
        {
            TrainAuthorizationException => error
                .WithMessage(TrainAuthorizationException.PublicMessage)
                .WithCode("TRAX_AUTHORIZATION"),
            TrainNotFoundException ex => error
                .WithMessage(ex.Message)
                .WithCode("TRAX_TRAIN_NOT_FOUND"),
            AmbiguousTrainNameException ex => error
                .WithMessage(ex.Message)
                .WithCode("TRAX_AMBIGUOUS_TRAIN"),
            TrainInputValidationException ex => error
                .WithMessage(ex.Message)
                .WithCode("TRAX_INVALID_INPUT"),
            TrainException ex => error
                .WithMessage(PublicTrainMessage(ex.Message))
                .WithCode("TRAX_TRAIN_ERROR"),
            _ => error,
        };
    }

    /// <summary>
    /// Prefixes of the messages the scheduler's remote run executors build around a worker's
    /// reply. What follows them is the worker's response, not text a train author wrote.
    /// </summary>
    private static readonly string[] RemoteTransportPrefixes =
    [
        "Remote run endpoint returned",
        "Remote train execution failed",
    ];

    /// <summary>
    /// The part of a <see cref="TrainException"/> message a client may see.
    /// </summary>
    internal static string PublicTrainMessage(string message)
    {
        if (TryReadCarried(message, out var carried))
            return
                string.Equals(carried.Type, nameof(TrainException), StringComparison.Ordinal)
                && !string.IsNullOrEmpty(carried.Message)
                ? PublicTrainMessage(carried.Message)
                : TrainFailedMessage;

        foreach (var prefix in RemoteTransportPrefixes)
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return TrainFailedMessage;

        return message;
    }

    private static bool TryReadCarried(string message, out TrainExceptionData carried)
    {
        carried = null!;
        if (!message.AsSpan().TrimStart().StartsWith("{"))
            return false;

        try
        {
            var data = JsonSerializer.Deserialize<TrainExceptionData>(message);
            if (data is null || string.IsNullOrEmpty(data.Type))
                return false;
            carried = data;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
