using System.Text.Json.Serialization;

namespace Trax.Core.Exceptions;

/// <summary>
/// Structured data about an exception that occurs within a workflow step.
/// Used for serializing exception information with proper JSON escaping.
/// </summary>
public class WorkflowExceptionData
{
    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; set; }

    [JsonPropertyName("workflowExternalId")]
    public required string WorkflowExternalId { get; set; }

    /// <summary>
    /// The type of exception that occurred (e.g., "InvalidOperationException").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// The name of the workflow step where the exception occurred.
    /// </summary>
    [JsonPropertyName("step")]
    public required string Step { get; set; }

    /// <summary>
    /// The error message from the original exception.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}
