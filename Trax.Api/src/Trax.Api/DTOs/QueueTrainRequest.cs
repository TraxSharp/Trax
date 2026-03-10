using System.Text.Json;

namespace Trax.Api.DTOs;

/// <summary>
/// Request to queue a train for background execution via the scheduler.
/// </summary>
/// <param name="TrainName">The interface FullName of the train to queue (e.g., <c>"MyApp.Trains.IMyTrain"</c>).</param>
/// <param name="Input">The JSON input payload to deserialize as the train's input type.</param>
/// <param name="Priority">Optional priority (higher values are processed first). Defaults to <c>null</c> (normal priority).</param>
public record QueueTrainRequest(string TrainName, JsonElement Input, int? Priority = null);
