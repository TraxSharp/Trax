using System.Text.Json;

namespace Trax.Api.DTOs;

public record ScheduleOnceRequest(string TrainName, JsonElement Input, TimeSpan Delay);
