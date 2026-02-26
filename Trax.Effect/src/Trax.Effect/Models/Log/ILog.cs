using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Models.Log;

public interface ILog : IModel
{
    [Column("id")]
    public new long Id { get; }

    [Column("metadata_id")]
    [JsonInclude]
    public long MetadataId { get; }

    [Column("level")]
    public LogLevel Level { get; set; }

    [Column("message")]
    public string Message { get; set; }
}
