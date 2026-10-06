namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// HTTP wire contract for the response from a remote job execution endpoint.
/// </summary>
/// <remarks>
/// On success, <see cref="MetadataId"/> carries the execution record ID.
/// On failure, <see cref="IsError"/> is true and the error fields carry failure details.
/// </remarks>
/// <param name="MetadataId">The Metadata ID of the execution (0 if metadata was not created)</param>
/// <param name="IsError">Whether the job execution failed</param>
/// <param name="ErrorMessage">Error message when <paramref name="IsError"/> is true</param>
/// <param name="ExceptionType">The .NET exception type name (e.g., "InvalidOperationException") when <paramref name="IsError"/> is true</param>
/// <param name="StackTrace">The remote stack trace when <paramref name="IsError"/> is true</param>
public record RemoteJobResponse(
    long MetadataId,
    bool IsError = false,
    string? ErrorMessage = null,
    string? ExceptionType = null,
    string? StackTrace = null
);
