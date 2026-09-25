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

    /// <summary>
    /// The id of the <c>trax.metadata</c> row for the run this junction is executing in, so a
    /// junction can read its own run back by primary key.
    /// </summary>
    /// <remarks>
    /// The same value for every execution path, because each runs under the metadata row it was
    /// dispatched with: a direct RUN, a queued run on a local worker, and a remote or Lambda run.
    /// <para>
    /// Prefer it to <see cref="TrainExternalId"/> for identifying a run. <c>metadata.external_id</c>
    /// has no unique index, so a dispatch retry leaves several rows sharing one external id, the
    /// older ones failed; and the column is unindexed, so looking a run up by it scans the largest
    /// table Trax writes.
    /// </para>
    /// <para>
    /// <b>Zero when the run was never persisted.</b> A train run with no data provider registered
    /// keeps its metadata in memory, so there is no row and no id, exactly as
    /// <c>Metadata.Id</c> is unset for the throwaway metadata passed to <c>OnQueue</c>.
    /// </para>
    /// <para>
    /// <b>Read it inside <c>Run</c>, and nowhere else.</b> The junction's <c>Metadata</c> is
    /// assigned per execution, so a junction registered as a singleton and reached through
    /// <c>IChain</c> has it overwritten by whichever concurrent run assigned it last. A junction
    /// that uses this value to decide whether to perform a side effect must be registered scoped or
    /// transient.
    /// </para>
    /// </remarks>
    [Column("train_metadata_id")]
    public long TrainMetadataId { get; private set; }

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
            TrainMetadataId = metadata.Id,
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
