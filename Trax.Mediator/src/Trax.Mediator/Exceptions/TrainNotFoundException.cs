namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService"/> when a caller
/// references a train name that does not match any registered train.
/// </summary>
/// <remarks>
/// The public <see cref="Exception.Message"/> is intentionally generic and never
/// contains the requested train name. Enumerating registered trains via error
/// messages would let unauthenticated callers probe the API surface. Diagnostic
/// detail lives in <see cref="RequestedName"/> for server-side logging only.
/// </remarks>
public class TrainNotFoundException : InvalidOperationException
{
    /// <summary>
    /// The name the caller passed, exactly as given. Caller-controlled: log it server-side, but do
    /// not echo it into a response.
    /// </summary>
    public string RequestedName { get; }

    /// <summary>
    /// Creates the exception. The message is always "The requested train was not found."; the
    /// name goes to <see cref="RequestedName"/> only.
    /// </summary>
    /// <param name="requestedName">The name that matched no registered train.</param>
    public TrainNotFoundException(string requestedName)
        : base("The requested train was not found.")
    {
        RequestedName = requestedName;
    }
}
