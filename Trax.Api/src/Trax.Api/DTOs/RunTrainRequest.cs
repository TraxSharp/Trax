using System.Text.Json;

namespace Trax.Api.DTOs;

/// <summary>
/// Request to execute a train synchronously and return its result inline.
/// </summary>
/// <param name="TrainName">The interface FullName of the train to run (e.g., <c>"MyApp.Trains.IMyTrain"</c>).</param>
/// <param name="Input">The JSON input payload to deserialize as the train's input type.</param>
public record RunTrainRequest(string TrainName, JsonElement Input);
