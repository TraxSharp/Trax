using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.Log;

/// <summary>
/// A row of <c>trax.log</c>: one <c>ILogger</c> message captured by the data-context logger that
/// <c>AddDataContextLogging</c> registers, queryable through <c>IDataContext.Logs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written in batches from a bounded background channel, filtered by the configured
/// minimum level and category blacklist. When the channel is full the oldest pending entries are
/// dropped, so the table is not a complete record under load. EF Core's own SQL command log is
/// never captured.
/// </para>
/// <para>
/// There is no timestamp column: order by <see cref="Id"/>. <see cref="MetadataId"/> is never
/// written, so a row cannot be joined to the run that logged it; filter on <see cref="Category"/>
/// or <see cref="Message"/> instead. See
/// https://traxsharp.net/docs/effect/debugging-with-the-log-table.
/// </para>
/// </remarks>
public class Log : ILog
{
    #region Columns

    /// <summary>
    /// Database-generated primary key. The insert order, and the only ordering the table has.
    /// </summary>
    [Column("id")]
    [JsonPropertyName("id")]
    public long Id { get; private set; }

    /// <summary>
    /// Intended reference to <c>trax.metadata.id</c>, but nothing on the write path sets it, so it
    /// is always 0 and <see cref="Metadata"/> never loads. Filtering logs by run through it returns
    /// nothing.
    /// </summary>
    [Column("metadata_id")]
    [JsonPropertyName("metadata_id")]
    [JsonInclude]
    public long MetadataId { get; private set; }

    /// <summary>The numeric <c>EventId.Id</c> passed to the logging call; 0 when none was given.</summary>
    [Column("event_id")]
    [JsonPropertyName("event_id")]
    public int EventId { get; set; }

    /// <summary>
    /// The level the message was logged at. Stored in the <c>trax.log_level</c> Postgres enum,
    /// whose labels are lowercase (<c>'error'</c>, not <c>'Error'</c>) and ordered by severity.
    /// </summary>
    [Column("level")]
    [JsonPropertyName("level")]
    public LogLevel Level { get; set; }

    /// <summary>
    /// The formatted message with NUL characters removed, truncated to 4000 UTF-16 units without
    /// splitting a surrogate pair.
    /// </summary>
    [Column("message")]
    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;

    /// <summary>
    /// The logger category, normally the FullName of the implementation type the logger was created
    /// for (not a train's interface name). Truncated to 500 UTF-16 units.
    /// </summary>
    [Column("category")]
    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;

    /// <summary>
    /// The logged exception's <c>Message</c>, truncated to 2000 UTF-16 units; null when the call
    /// passed no exception. The exception type is not stored.
    /// </summary>
    [Column("exception")]
    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    /// <summary>
    /// The logged exception's stack trace, truncated to 4000 UTF-16 units; null when no exception
    /// was passed or it was never thrown.
    /// </summary>
    [Column("stack_trace")]
    [JsonPropertyName("stack_trace")]
    public string? StackTrace { get; set; }

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Navigation to the run through <see cref="MetadataId"/>. Because that column is never
    /// written, this is null on every row Trax writes, despite the non-nullable annotation.
    /// </summary>
    public Metadata.Metadata Metadata { get; set; } = null!;

    #endregion

    #region Functions

    /// <summary>
    /// Builds an unsaved row from one logging call, removing NUL characters (which Postgres text
    /// refuses) and truncating each text field to its limit without splitting a surrogate pair.
    /// </summary>
    /// <param name="createLog">The logging call's values.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Log Create(CreateLog createLog)
    {
        var newLog = new Log()
        {
            Level = createLog.Level,
            Message = Truncate(createLog.Message, 4000)!,
            Category = Truncate(createLog.CategoryName, 500)!,
            EventId = createLog.EventId,
            Exception = Truncate(createLog.Exception?.Message, 2000),
            StackTrace = Truncate(createLog.Exception?.StackTrace, 4000),
        };

        return newLog;
    }

    /// <summary>
    /// Makes a text field storable: drops NUL, which Postgres text columns refuse, and cuts to
    /// <paramref name="maxLength"/> UTF-16 units without splitting a surrogate pair, which the
    /// database driver cannot encode. An entry it cannot store fails its whole flush batch.
    /// </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
            return null;

        if (value.Contains('\0'))
            value = value.Replace("\0", string.Empty);

        if (value.Length <= maxLength)
            return value;

        var cut = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..cut];
    }

    /// <summary>
    /// Serializes this log row to JSON for a log line. Not a stable format: read the
    /// properties for values.
    /// </summary>
    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            TraxLogSerialization.ForLogging(
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            )
        );

    #endregion

    /// <summary>
    /// For JSON deserialization and EF Core materialization. Build a new row with
    /// <see cref="Create"/>, which applies the length limits.
    /// </summary>
    [JsonConstructor]
    public Log() { }
}
