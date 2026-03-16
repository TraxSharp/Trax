using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.JunctionMetadata.DTOs;

namespace Trax.Effect.Models.JunctionMetadata;

public class JunctionMetadata : IModel
{
    #region Columns

    [Column("id")]
    public long Id { get; private set; }

    [Column("train_name")]
    public string TrainName { get; private set; } = null!;

    [Column("name")]
    public string Name { get; private set; } = null!;

    [Column("external_id")]
    public string ExternalId { get; private set; } = null!;

    [Column("train_external_id")]
    public string TrainExternalId { get; private set; } = null!;

    [Column("start_time_utc")]
    public DateTime? StartTimeUtc { get; set; }

    [Column("end_time_utc")]
    public DateTime? EndTimeUtc { get; set; }

    [Column("input_type")]
    public Type InputType { get; private set; } = null!;

    [Column("output_type")]
    public Type OutputType { get; private set; } = null!;

    [Column("state")]
    public EitherStatus State { get; set; }

    [Column("has_ran")]
    public bool HasRan { get; set; }

    [Column("output_json")]
    public string? OutputJson { get; set; }

    #endregion

    #region ForeignKeys

    #endregion

    #region Functions

    public static JunctionMetadata Create(
        CreateJunctionMetadata junctionMetadata,
        Metadata.Metadata metadata
    )
    {
        var newJunctionMetadata = new JunctionMetadata
        {
            Name = junctionMetadata.Name,
            ExternalId = junctionMetadata.ExternalId,
            TrainExternalId = metadata.ExternalId,
            TrainName = metadata.Name,
            StartTimeUtc = junctionMetadata.StartTimeUtc,
            EndTimeUtc = junctionMetadata.EndTimeUtc,
            InputType = junctionMetadata.InputType,
            OutputType = junctionMetadata.OutputType,
            State = junctionMetadata.State,
            HasRan = false,
        };

        return newJunctionMetadata;
    }

    public override string ToString() =>
        JsonSerializer.Serialize(this, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    #endregion

    [JsonConstructor]
    public JunctionMetadata() { }
}
