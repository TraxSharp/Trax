namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// HTTP wire contract for dispatching a job to a remote endpoint.
/// </summary>
/// <remarks>
/// This is the JSON payload sent by <see cref="HttpJobSubmitter"/> and received by
/// the <c>UseTraxJobRunner()</c> endpoint. It differs from
/// <see cref="Trains.JobRunner.RunJobRequest"/> because the input must be transmitted
/// as a serialized JSON string with its type name for deserialization on the remote side.
/// </remarks>
/// <param name="MetadataId">The ID of the Metadata record to execute</param>
/// <param name="Input">Optional JSON-serialized train input</param>
/// <param name="InputType">Fully-qualified type name for deserializing <paramref name="Input"/></param>
public record RemoteJobRequest(long MetadataId, string? Input = null, string? InputType = null);
