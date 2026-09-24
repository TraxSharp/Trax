using System.Text.Json.Serialization;

namespace Trax.Core.Exceptions;

/// <summary>
/// Structured data about an exception that occurs within a train junction.
/// Used for serializing exception information with proper JSON escaping.
/// </summary>
public class TrainExceptionData
{
    /// <summary>
    /// How the failure was classified where it happened, or null when nothing classified it.
    /// </summary>
    /// <remarks>
    /// Carried here so a remotely-executed run's classification survives the trip home. The worker
    /// classifies while it still holds the real exception; the API side reads the result rather
    /// than trying to re-classify a reconstruction, which would be matching on a type name.
    /// </remarks>
    [JsonPropertyName("failureClass")]
    public FailureClass? FailureClass { get; set; }

    [JsonPropertyName("trainName")]
    public required string TrainName { get; set; }

    [JsonPropertyName("trainExternalId")]
    public required string TrainExternalId { get; set; }

    /// <summary>
    /// The type of exception that occurred (e.g., "InvalidOperationException").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// The name of the train junction where the exception occurred.
    /// </summary>
    [JsonPropertyName("junction")]
    public required string Junction { get; set; }

    /// <summary>
    /// The error message from the original exception.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// The original stack trace from where the exception was thrown.
    /// Nullable for backwards compatibility with previously serialized data.
    /// </summary>
    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }
}
