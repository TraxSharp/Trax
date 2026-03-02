namespace Trax.Scheduler.Trains.TaskServerExecutor;

/// <summary>
/// Request input for the TaskServerExecutorTrain.
/// </summary>
/// <param name="MetadataId">The ID of the Metadata record to execute</param>
/// <param name="Input">
/// Optional train input object for ad-hoc executions (e.g., from the dashboard).
/// When null, the input is resolved from the associated Manifest's properties.
/// </param>
public record ExecuteManifestRequest(long MetadataId, object? Input = null);
