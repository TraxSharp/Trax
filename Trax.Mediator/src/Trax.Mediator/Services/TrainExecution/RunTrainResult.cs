namespace Trax.Mediator.Services.TrainExecution;

/// <summary>A completed run from <see cref="ITrainExecutionService.RunAsync(string, string, CancellationToken)"/> or <c>IRunExecutor</c>.</summary>
/// <param name="MetadataId">The id of the run's metadata record.</param>
/// <param name="ExternalId">The run's external id.</param>
/// <param name="Output">
/// The train's output, typed as its <c>TOut</c>; null for a train whose output is <c>Unit</c>.
/// </param>
public record RunTrainResult(long MetadataId, string ExternalId, object? Output = null);
