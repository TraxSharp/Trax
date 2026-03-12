namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// HTTP wire contract for the response from a remote run endpoint.
/// </summary>
/// <remarks>
/// On success, <see cref="OutputJson"/> and <see cref="OutputType"/> carry the serialized train output.
/// On failure, <see cref="IsError"/> is true and <see cref="ErrorMessage"/> carries the error details.
/// The <see cref="MetadataId"/> is included when available so the caller can reference the execution record.
/// </remarks>
/// <param name="MetadataId">The Metadata ID of the execution (0 if metadata was not created)</param>
/// <param name="ExternalId">The external ID of the execution (null on error)</param>
/// <param name="OutputJson">JSON-serialized train output (null for Unit trains or errors)</param>
/// <param name="OutputType">Fully-qualified type name of the output (null for Unit trains or errors)</param>
/// <param name="IsError">Whether the train execution failed</param>
/// <param name="ErrorMessage">Error message when <paramref name="IsError"/> is true</param>
public record RemoteRunResponse(
    long MetadataId,
    string? ExternalId = null,
    string? OutputJson = null,
    string? OutputType = null,
    bool IsError = false,
    string? ErrorMessage = null
);
