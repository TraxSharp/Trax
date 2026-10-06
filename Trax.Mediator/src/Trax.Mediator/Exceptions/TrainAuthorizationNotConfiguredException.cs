namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService"/> when a train that
/// declares <c>[TraxAuthorize]</c> is run or queued on a host with no
/// <c>ITrainAuthorizationService</c> registered, outside a trusted execution scope, and without
/// <c>AllowMissingAuthorizationService()</c>.
/// </summary>
/// <remarks>
/// This is a misconfiguration of the host, not a refusal of the caller or of the input, so a
/// surface that reports enqueue refusals to clients can report it as a server fault instead. It
/// derives from <see cref="InvalidOperationException"/>, the type thrown here before it existed,
/// so existing catches still see it.
/// </remarks>
public class TrainAuthorizationNotConfiguredException : InvalidOperationException
{
    /// <summary>The canonical name of the train that was refused.</summary>
    public string TrainName { get; }

    /// <summary>Creates the exception with the message the caller built.</summary>
    /// <param name="trainName">The canonical name of the refused train.</param>
    /// <param name="message">
    /// The full message. The mediator's names the train and says how to fix the host: register an
    /// <c>ITrainAuthorizationService</c> or opt out with <c>AllowMissingAuthorizationService()</c>.
    /// </param>
    public TrainAuthorizationNotConfiguredException(string trainName, string message)
        : base(message)
    {
        TrainName = trainName;
    }
}
