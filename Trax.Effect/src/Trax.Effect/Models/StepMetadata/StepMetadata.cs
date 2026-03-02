using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.StepMetadata.DTOs;

namespace Trax.Effect.Models.StepMetadata;

public class StepMetadata : IModel
{
    #region Columns

    [Column("id")]
    public long Id { get; private set; }

    [Column("train_name")]
    public string TrainName { get; private set; }

    [Column("name")]
    public string Name { get; private set; }

    [Column("external_id")]
    public string ExternalId { get; private set; }

    [Column("train_external_id")]
    public string TrainExternalId { get; private set; }

    [Column("start_time_utc")]
    public DateTime? StartTimeUtc { get; set; }

    [Column("end_time_utc")]
    public DateTime? EndTimeUtc { get; set; }

    [Column("input_type")]
    public Type InputType { get; private set; }

    [Column("output_type")]
    public Type OutputType { get; private set; }

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

    public static StepMetadata Create(CreateStepMetadata stepMetadata, Metadata.Metadata metadata)
    {
        var newStepMetadata = new StepMetadata
        {
            Name = stepMetadata.Name,
            ExternalId = stepMetadata.ExternalId,
            TrainExternalId = metadata.ExternalId,
            TrainName = metadata.Name,
            StartTimeUtc = stepMetadata.StartTimeUtc,
            EndTimeUtc = stepMetadata.EndTimeUtc,
            InputType = stepMetadata.InputType,
            OutputType = stepMetadata.OutputType,
            State = stepMetadata.State,
            HasRan = false,
        };

        return newStepMetadata;
    }

    public override string ToString() =>
        JsonSerializer.Serialize(this, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    #endregion

    [JsonConstructor]
    public StepMetadata() { }
}
