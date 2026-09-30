using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.JunctionMetadata;

/// <summary>
/// The in-memory record of one junction's execution inside a service train, exposed as
/// <c>EffectJunction.Metadata</c> and handed to junction effect providers (the junction logger,
/// the progress provider) before and after the junction runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not persisted.</b> There is no <c>junction_metadata</c> table and no <c>DbSet</c> for it; the
/// <c>[Column]</c> names only shape its JSON. What reaches the database about junctions is the run's
/// <c>Metadata.CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c> (written by the progress
/// provider) and <c>FailureJunction</c> on failure.
/// </para>
/// <para>
/// A new instance is created every time the junction is reached, so read it during or right after
/// that execution. A junction registered as a singleton shares the property across concurrent runs.
/// </para>
/// </remarks>
public class JunctionMetadata : IModel
{
    #region Columns

    /// <summary>
    /// Always zero: junction metadata is never saved, so no key is generated. Present to satisfy
    /// <see cref="IModel"/>.
    /// </summary>
    [Column("id")]
    public long Id { get; private set; }

    /// <summary>
    /// The owning run's <c>Metadata.Name</c>: the train's canonical name, normally its interface
    /// FullName.
    /// </summary>
    [Column("train_name")]
    public string TrainName { get; private set; } = null!;

    /// <summary>
    /// The junction class's short name (<c>GetType().Name</c>, no namespace). The same value is
    /// written to the run's <c>CurrentlyRunningJunction</c> by the progress provider.
    /// </summary>
    [Column("name")]
    public string Name { get; private set; } = null!;

    /// <summary>
    /// A fresh 32-digit GUID (<c>"N"</c> format) for this one execution of the junction. Nothing
    /// else stores it; use it to correlate the before and after log lines of one execution.
    /// </summary>
    [Column("external_id")]
    public string ExternalId { get; private set; } = null!;

    /// <summary>
    /// The owning run's <c>Metadata.ExternalId</c>. Not unique across rows of <c>trax.metadata</c>;
    /// prefer <see cref="TrainMetadataId"/> to identify the run.
    /// </summary>
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

    /// <summary>
    /// UTC time the junction started: stamped after the before-junction effects (logging, progress,
    /// cancellation check) have run. Null while those effects run.
    /// </summary>
    [Column("start_time_utc")]
    public DateTime? StartTimeUtc { get; set; }

    /// <summary>
    /// UTC time the junction returned, set before the after-junction effects run. Stays null if the
    /// junction threw instead of returning (for example on cancellation).
    /// </summary>
    [Column("end_time_utc")]
    public DateTime? EndTimeUtc { get; set; }

    /// <summary>The junction's <c>TIn</c> type argument.</summary>
    [Column("input_type")]
    public Type InputType { get; private set; } = null!;

    /// <summary>The junction's <c>TOut</c> type argument.</summary>
    [Column("output_type")]
    public Type OutputType { get; private set; } = null!;

    /// <summary>
    /// The railway's state: on creation, the state arriving from the previous junction; once the
    /// junction returns, the state of its own result (<see cref="EitherStatus.IsLeft"/> when it or
    /// an earlier junction failed).
    /// </summary>
    [Column("state")]
    public EitherStatus State { get; set; }

    /// <summary>
    /// True once the junction's <c>Run</c> has been called and its railway step has returned,
    /// whether <c>Run</c> succeeded or failed; check <see cref="State"/> for which. False when the
    /// junction was skipped because an earlier junction had already failed, and when the step threw.
    /// </summary>
    [Column("has_ran")]
    public bool HasRan { get; set; }

    /// <summary>
    /// The junction's successful output as JSON, set by the junction logger's after-junction effect
    /// when <c>SerializeJunctionData</c> is enabled. Null when the junction failed or was skipped,
    /// when its output was null, when serialization is off, or when the junction logger is not
    /// registered.
    /// </summary>
    [Column("output_json")]
    public string? OutputJson { get; set; }

    #endregion

    #region ForeignKeys

    #endregion

    #region Functions

    /// <summary>
    /// Builds the record for one junction execution, copying the run's name, external id and id
    /// from <paramref name="metadata"/>. <see cref="HasRan"/> starts false.
    /// </summary>
    /// <param name="junctionMetadata">The junction's own values.</param>
    /// <param name="metadata">The run the junction executes in.</param>
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

    /// <summary>
    /// Serializes this junction record to JSON for a log line. Not a stable format: read the
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
    /// For JSON deserialization. Build a new record with <see cref="Create"/>.
    /// </summary>
    [JsonConstructor]
    public JunctionMetadata() { }
}
