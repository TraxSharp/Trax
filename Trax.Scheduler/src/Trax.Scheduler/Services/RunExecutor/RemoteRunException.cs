using Trax.Core.Exceptions;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// A run sent to a remote runner failed, either on the runner or on the way there and back.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is the full account the calling side records: the runner's
/// structured failure as <see cref="TrainExceptionData"/> JSON, or the transport failure with the
/// runner's reply. Neither was written for a client. <see cref="PublicMessage"/> is the part that
/// was: the runner sets it only from a train author's own <see cref="TrainException"/>, so a
/// surface that shows errors to clients (the GraphQL error filter) shows this, and when it is
/// null says only that the train failed. See
/// <c>Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md</c>.
/// </remarks>
public class RemoteRunException(string message, string? publicMessage = null)
    : TrainException(message)
{
    /// <summary>
    /// The message the runner offered for a client, or null when the failure was anything but a
    /// train author's <see cref="TrainException"/>: another exception, a transport failure, or a
    /// runner too old to send one.
    /// </summary>
    public string? PublicMessage { get; } = publicMessage;
}
