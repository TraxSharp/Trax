namespace Trax.Api.Exceptions;

/// <summary>
/// Thrown when a user is not authorized to execute a specific train.
/// </summary>
/// <remarks>
/// The public <see cref="Exception.Message"/> is intentionally generic
/// (<c>"Not authorized."</c>) and carries no information about the train,
/// policy, or role. Unauthenticated clients that probe the API must not be
/// able to enumerate protected trains or their requirements via error messages.
/// Diagnostic detail is available on <see cref="TrainName"/> and
/// <see cref="Reason"/> for server-side logging only; the GraphQL error filter
/// never forwards those fields to the client.
/// </remarks>
public class TrainAuthorizationException : UnauthorizedAccessException
{
    public const string PublicMessage = "Not authorized.";

    public string TrainName { get; }
    public string Reason { get; }

    public TrainAuthorizationException(string trainName, string reason)
        : base(PublicMessage)
    {
        TrainName = trainName;
        Reason = reason;
    }
}
