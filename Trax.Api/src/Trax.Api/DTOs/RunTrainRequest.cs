using System.Text.Json;

namespace Trax.Api.DTOs;

public record RunTrainRequest(string TrainName, JsonElement Input);
