using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.Exceptions;
using Trax.Core.Exceptions;
using Trax.Mediator.Exceptions;
using Trax.Scheduler.Services.RunExecutor;

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
/// <see cref="NoTrainForInputException"/>: the host is not built to run what the caller asked
/// for, because no registered train takes the input; code <c>TRAX_HOST_CONFIGURATION</c>. The
/// public message is always <see cref="TrainNotRunMessage"/>, and HotChocolate's exception detail
/// is dropped even when the host switched it on: the exception names the input type and the
/// assemblies the host scanned, which describe how the host is built. The full exception is
/// logged as an error, because the fix belongs to whoever runs the host.
/// </item>
/// <item>
/// <see cref="TrainException"/>: execution failures; code <c>TRAX_TRAIN_ERROR</c>. Only an exact
/// <see cref="TrainException"/>'s message is a train author's: a subclass, Trax's own or a
/// consumer's, gets <see cref="TrainFailedMessage"/>, as the operations service's
/// <c>queueTrain</c> and <c>runTrain</c> refusals do. A
/// message a train author wrote is passed through, and authors are expected to treat it as
/// client-safe. A message that carries another exception, as the structured
/// <see cref="TrainExceptionData"/> JSON a nested train produces, passes through only the
/// carried message of a carried <see cref="TrainException"/>; any other carried type gets
/// <see cref="TrainFailedMessage"/>. A run on a remote runner fails as a
/// <see cref="RemoteRunException"/>, whose <see cref="RemoteRunException.PublicMessage"/> the
/// runner chose (<c>Trax.Docs/adr/0028</c>): that message is shown, and
/// <see cref="TrainFailedMessage"/> when there is none, transport failures included. The detail
/// stays in the recorded metadata and the logs. See
/// <c>docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md</c>.
/// </item>
/// </list>
/// Every mapped case detaches the exception from the error, so HotChocolate's exception details,
/// when a host switches them on, add nothing to the public shape above.
/// Any other exception type (including <see cref="InvalidOperationException"/>) is
/// left with HotChocolate's default masked message. Use the typed exceptions above
/// when you want a message to reach the client.
/// </remarks>
internal class TraxErrorFilter(ILogger<TraxErrorFilter>? logger = null) : IErrorFilter
{
    private readonly ILogger _logger = logger ?? NullLogger<TraxErrorFilter>.Instance;

    /// <summary>
    /// What a client is told when the host has no train for the input it was sent.
    /// </summary>
    internal const string TrainNotRunMessage = "The train could not be run.";

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

        // Every mapped case detaches the exception: HotChocolate writes an attached exception's
        // message and stack trace into the response when the host switches exception details on,
        // after the filters have run, so a mapped error would otherwise carry everything its
        // public message leaves out. An unmapped exception keeps it, as HotChocolate's own
        // development detail.
        return error.Exception switch
        {
            TrainAuthorizationException => Public(
                error,
                TrainAuthorizationException.PublicMessage,
                "TRAX_AUTHORIZATION"
            ),
            TrainNotFoundException ex => Public(error, ex.Message, "TRAX_TRAIN_NOT_FOUND"),
            AmbiguousTrainNameException ex => Public(error, ex.Message, "TRAX_AMBIGUOUS_TRAIN"),
            TrainInputValidationException ex => Public(error, ex.Message, "TRAX_INVALID_INPUT"),
            NoTrainForInputException ex => HostConfiguration(error, ex),
            // Ahead of the TrainException arm it derives from: the runner chose what a client
            // may read, and the full message is the calling side's record of the failure.
            RemoteRunException ex => Public(
                error,
                ex.PublicMessage ?? TrainFailedMessage,
                "TRAX_TRAIN_ERROR"
            ),
            // Only an exact TrainException's message is a train author's. A subclass carries
            // whatever its author put in it (Trax's own TrainAlreadyStartedException names a
            // metadata id and the train's FullName), so it reads as a failed train, the same rule
            // the operations service applies to a queueTrain or runTrain refusal.
            TrainException ex when ex.GetType() == typeof(TrainException) => Public(
                error,
                PublicTrainMessage(ex.Message),
                "TRAX_TRAIN_ERROR"
            ),
            TrainException => Public(error, TrainFailedMessage, "TRAX_TRAIN_ERROR"),
            _ => error,
        };
    }

    private static IError Public(IError error, string message, string code) =>
        error.WithMessage(message).WithCode(code).WithException(null);

    private IError HostConfiguration(IError error, NoTrainForInputException exception)
    {
        _logger.LogError(
            exception,
            "A GraphQL request asked for a train this host cannot run: no registered train takes "
                + "{InputType}. Scanned assemblies: {ScannedAssemblies}.",
            exception.InputType.FullName,
            exception.ScannedAssemblies
        );

        // Detached once logged, like every mapped case.
        return Public(error, TrainNotRunMessage, "TRAX_HOST_CONFIGURATION");
    }

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
