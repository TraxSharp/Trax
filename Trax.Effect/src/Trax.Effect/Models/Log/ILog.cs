using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Models.Log;

/// <summary>
/// The shape of a row in <c>trax.log</c>. Infrastructure implemented only by <see cref="Log"/>;
/// not intended to be used directly: query <see cref="Log"/> instead.
/// </summary>
internal interface ILog : IModel
{
    /// <summary>Database-generated primary key; its order is the only time axis the table has.</summary>
    [Column("id")]
    public new long Id { get; }

    /// <summary>
    /// Intended reference to <c>trax.metadata.id</c>. Nothing on the write path sets it, so it is
    /// always 0.
    /// </summary>
    [Column("metadata_id")]
    [JsonInclude]
    public long MetadataId { get; }

    /// <summary>The level the message was logged at.</summary>
    [Column("level")]
    public LogLevel Level { get; set; }

    /// <summary>The formatted log message, at most 4000 characters.</summary>
    [Column("message")]
    public string Message { get; set; }
}
