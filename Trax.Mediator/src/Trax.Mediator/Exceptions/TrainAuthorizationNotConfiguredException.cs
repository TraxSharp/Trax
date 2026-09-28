namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService"/> when a train that
/// declares <c>[TraxAuthorize]</c> is run or queued on a host with no
/// <c>ITrainAuthorizationService</c> registered, outside a trusted execution scope, and without
/// <c>AllowMissingAuthorizationService()</c>. See mediator/0001.
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

    public TrainAuthorizationNotConfiguredException(string trainName, string message)
        : base(message)
    {
        TrainName = trainName;
    }
}
