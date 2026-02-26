using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;
using Trax.Effect.Models.Log.DTOs;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Models.Log;

public class Log : ILog
{
    #region Columns

    [Column("id")]
    [JsonPropertyName("id")]
    public long Id { get; private set; }

    [Column("metadata_id")]
    [JsonPropertyName("metadata_id")]
    [JsonInclude]
    public long MetadataId { get; private set; }

    [Column("event_id")]
    [JsonPropertyName("event_id")]
    public int EventId { get; set; }

    [Column("level")]
    [JsonPropertyName("level")]
    public LogLevel Level { get; set; }

    [Column("message")]
    [JsonPropertyName("message")]
    public string Message { get; set; }

    [Column("category")]
    [JsonPropertyName("category")]
    public string Category { get; set; }

    [Column("exception")]
    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    [Column("stack_trace")]
    [JsonPropertyName("stack_trace")]
    public string? StackTrace { get; set; }

    #endregion

    #region ForeignKeys

    public Metadata.Metadata Metadata { get; set; }

    #endregion

    #region Functions

    public static Log Create(CreateLog createLog)
    {
        var newLog = new Log()
        {
            Level = createLog.Level,
            Message = Truncate(createLog.Message, 4000)!,
            Category = Truncate(createLog.CategoryName, 500)!,
            EventId = createLog.EventId,
            Exception = Truncate(createLog.Exception?.Message, 2000),
            StackTrace = Truncate(createLog.Exception?.StackTrace, 4000)
        };

        return newLog;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value?.Length > maxLength ? value[..maxLength] : value;

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            Trax.CoreEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    #endregion

    [JsonConstructor]
    public Log() { }
}
