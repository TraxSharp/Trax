using System.Text.Json;
using Trax.Core.Exceptions;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// HTTP wire contract for the response from a remote run endpoint.
/// </summary>
/// <remarks>
/// On success, <see cref="OutputJson"/> and <see cref="OutputType"/> carry the serialized train output.
/// On failure, <see cref="IsError"/> is true and the structured error fields carry failure details.
/// The <see cref="MetadataId"/> is included when available so the caller can reference the execution record.
/// </remarks>
/// <param name="MetadataId">The Metadata ID of the execution (0 if metadata was not created)</param>
/// <param name="ExternalId">The external ID of the execution (null on error)</param>
/// <param name="OutputJson">JSON-serialized train output (null for Unit trains or errors)</param>
/// <param name="OutputType">Fully-qualified type name of the output (null for Unit trains or errors)</param>
/// <param name="IsError">Whether the train execution failed</param>
/// <param name="ErrorMessage">Error message when <paramref name="IsError"/> is true</param>
/// <param name="ExceptionType">The .NET exception type name (e.g., "InvalidOperationException") when <paramref name="IsError"/> is true</param>
/// <param name="FailureJunction">The train junction where the failure occurred, extracted from <c>TrainExceptionData</c> if available</param>
/// <param name="StackTrace">The remote stack trace when <paramref name="IsError"/> is true</param>
public record RemoteRunResponse(
    long MetadataId,
    string? ExternalId = null,
    string? OutputJson = null,
    string? OutputType = null,
    bool IsError = false,
    string? ErrorMessage = null,
    string? ExceptionType = null,
    string? FailureJunction = null,
    string? StackTrace = null,
    FailureClass? FailureClass = null
)
{
    /// <summary>
    /// Rebuilds the failure this response reports as the <see cref="TrainException"/> the calling
    /// side records.
    /// </summary>
    /// <remarks>
    /// When the worker sent structured error fields (<see cref="ExceptionType"/> and
    /// <see cref="FailureJunction"/>), the exception's message is the <see cref="TrainExceptionData"/>
    /// JSON, so <c>Metadata.AddException()</c> on the calling side parses it into its structured
    /// fields (FailureException, FailureJunction, FailureReason) and keeps the worker's
    /// <see cref="FailureClass"/>. Every executor that reads a remote response uses this, so the
    /// HTTP and Lambda paths record a remote failure the same way.
    /// </remarks>
    public TrainException ToTrainException()
    {
        if (ExceptionType is not null)
        {
            var data = new TrainExceptionData
            {
                TrainName = "",
                TrainExternalId = "",
                Type = ExceptionType,
                Junction = FailureJunction ?? "Unknown",
                Message = ErrorMessage ?? "Remote train execution failed",
                // Carried rather than recomputed: the original exception type is gone by now, so
                // re-classifying here would mean matching a type name.
                FailureClass = FailureClass,
            };

            return new TrainException(JsonSerializer.Serialize(data));
        }

        return new TrainException($"Remote train execution failed: {ErrorMessage}");
    }
}
