using System.Text.Json.Serialization;

namespace Trax.Core.Exceptions;

/// <summary>
/// Structured data about an exception that occurs within a train step.
/// Used for serializing exception information with proper JSON escaping.
/// </summary>
public class TrainExceptionData
{
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
    /// The name of the train step where the exception occurred.
    /// </summary>
    [JsonPropertyName("step")]
    public required string Step { get; set; }

    /// <summary>
    /// The error message from the original exception.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}
