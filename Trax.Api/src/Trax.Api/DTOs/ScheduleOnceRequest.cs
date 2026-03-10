using System.Text.Json;

namespace Trax.Api.DTOs;

/// <summary>
/// Request to schedule a one-time delayed train execution.
/// </summary>
/// <param name="TrainName">The interface FullName of the train to schedule (e.g., <c>"MyApp.Trains.IMyTrain"</c>).</param>
/// <param name="Input">The JSON input payload to deserialize as the train's input type.</param>
/// <param name="Delay">How long to wait before executing the train.</param>
public record ScheduleOnceRequest(string TrainName, JsonElement Input, TimeSpan Delay);
