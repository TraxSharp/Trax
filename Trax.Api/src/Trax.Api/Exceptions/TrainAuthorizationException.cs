namespace Trax.Api.Exceptions;

/// <summary>
/// Thrown when a user is not authorized to execute a specific train.
/// </summary>
public class TrainAuthorizationException : UnauthorizedAccessException
{
    public string TrainName { get; }

    public TrainAuthorizationException(string trainName, string reason)
        : base($"Authorization failed for train '{trainName}': {reason}")
    {
        TrainName = trainName;
    }
}
