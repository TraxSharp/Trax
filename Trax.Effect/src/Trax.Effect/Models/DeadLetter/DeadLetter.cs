using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter.DTOs;

namespace Trax.Effect.Models.DeadLetter;

/// <summary>
/// Represents a job execution that has been moved to the dead letter queue after exceeding retry limits.
/// </summary>
/// <remarks>
/// Dead letters are created when a job has failed too many times and requires manual intervention.
/// They serve as both an audit trail and a mechanism for operators to review, retry, or acknowledge
/// failed jobs.
///
/// A dead letter maintains a reference to its Manifest (job definition), preserving the full
/// context of what was being executed when the failure occurred.
/// </remarks>
public class DeadLetter : IModel
{
    #region Columns

    /// <summary>
    /// Gets the unique identifier for this dead letter record.
    /// </summary>
    [Column("id")]
    public long Id { get; private set; }

    /// <summary>
    /// Gets the time when this job was moved to the dead letter queue.
    /// </summary>
    [Column("dead_lettered_at")]
    public DateTime DeadLetteredAt { get; private set; }

    /// <summary>
    /// Gets or sets the current resolution status of this dead letter.
    /// </summary>
    [Column("status")]
    public DeadLetterStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the time when this dead letter was resolved (retried or acknowledged).
    /// </summary>
    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Gets or sets a note explaining the resolution (e.g., why it was acknowledged).
    /// </summary>
    [Column("resolution_note")]
    public string? ResolutionNote { get; set; }

    /// <summary>
    /// Gets the reason why this job was moved to the dead letter queue.
    /// </summary>
    [Column("reason")]
    public string Reason { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts made before this job was dead-lettered.
    /// </summary>
    [Column("retry_count_at_dead_letter")]
    public int RetryCountAtDeadLetter { get; private set; }

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Gets the ID of the associated Manifest (job definition).
    /// </summary>
    [Column("manifest_id")]
    public long ManifestId { get; private set; }

    /// <summary>
    /// Gets the associated Manifest containing the job definition.
    /// </summary>
    public Manifest.Manifest? Manifest { get; private set; }

    /// <summary>
    /// Gets or sets the ID of the retry Metadata record, if this dead letter was retried.
    /// </summary>
    [Column("retry_metadata_id")]
    public long? RetryMetadataId { get; set; }

    /// <summary>
    /// Gets the Metadata record for the retry execution, if applicable.
    /// </summary>
    public Metadata.Metadata? RetryMetadata { get; private set; }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new DeadLetter record for a failed job execution.
    /// </summary>
    /// <param name="createDeadLetter"></param>
    /// <returns>A new DeadLetter instance</returns>
    /// <remarks>
    /// Note: Only ManifestId is set, not the Manifest navigation property.
    /// This avoids EF Core tracking issues when the Manifest was loaded with .Include().
    /// </remarks>
    public static DeadLetter Create(CreateDeadLetter createDeadLetter)
    {
        return new DeadLetter
        {
            ManifestId = createDeadLetter.Manifest.Id,
            Manifest = createDeadLetter.Manifest,
            // Don't set Manifest navigation property to avoid EF Core tracking issues
            // when the manifest was loaded with includes
            DeadLetteredAt = DateTime.UtcNow,
            Reason = createDeadLetter.Reason,
            RetryCountAtDeadLetter = createDeadLetter.RetryCount,
            Status = DeadLetterStatus.AwaitingIntervention,
        };
    }

    /// <summary>
    /// Marks this dead letter as acknowledged without retrying.
    /// </summary>
    /// <param name="note">A note explaining the acknowledgement</param>
    public void Acknowledge(string note)
    {
        Status = DeadLetterStatus.Acknowledged;
        ResolvedAt = DateTime.UtcNow;
        ResolutionNote = note;
    }

    /// <summary>
    /// Marks this dead letter as retried.
    /// </summary>
    /// <param name="retryMetadataId">The ID of the new Metadata record for the retry</param>
    public void MarkRetried(long retryMetadataId)
    {
        Status = DeadLetterStatus.Retried;
        ResolvedAt = DateTime.UtcNow;
        RetryMetadataId = retryMetadataId;
    }

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    #endregion

    public DeadLetter() { }
}
