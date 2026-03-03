using System.Text.Json;

namespace Trax.Api.DTOs;

public record QueueTrainRequest(string TrainName, JsonElement Input, int? Priority = null);
