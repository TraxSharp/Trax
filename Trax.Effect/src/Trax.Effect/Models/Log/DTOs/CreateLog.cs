using Microsoft.Extensions.Logging;

namespace Trax.Effect.Models.Log.DTOs;

public class CreateLog
{
    public required LogLevel Level { get; set; }

    public required string Message { get; set; }

    public required string CategoryName { get; set; }

    public required int EventId { get; set; }

    public Exception? Exception { get; set; }
}
