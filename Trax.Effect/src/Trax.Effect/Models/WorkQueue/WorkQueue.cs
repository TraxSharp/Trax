using System.Buffers;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.WorkQueue;

/// <summary>
/// Represents a queued train execution — the intermediary between scheduling and dispatch.
/// </summary>
/// <remarks>
/// A WorkQueue entry decouples "intent to run" from "actual execution". All sources
/// (manifest scheduling, dashboard triggers, re-runs) create WorkQueue entries, and the
/// JobDispatcher picks from the queue to create Metadata records and enqueue to the
/// background task server.
/// </remarks>
public class WorkQueue : IModel
{
    /// <summary>
    /// The lowest dispatch priority. <see cref="Create"/> clamps a lower requested priority up to it.
    /// </summary>
    public const int MinPriority = 0;

    /// <summary>
    /// The highest dispatch priority. <see cref="Create"/> clamps a higher requested priority down
    /// to it.
    /// </summary>
    public const int MaxPriority = 31;

    /// <summary>
    /// The longest <see cref="SubjectKey"/> an entry accepts, in Unicode characters (runes), not
    /// UTF-16 units: a character outside the Basic Multilingual Plane counts once, although it
    /// takes two units. A character is at most four bytes in UTF-8, so the longest key is 2048
    /// bytes, inside the 2704-byte Postgres btree entry limit. A key too long for the index inserts
    /// fine while queued and then fails the claim on every dispatch cycle, so it is refused when
    /// the entry is created.
    /// </summary>
    public const int MaxSubjectKeyLength = 512;

    #region Columns

    /// <summary>
    /// Database-generated primary key. Zero until the entry is inserted.
    /// </summary>
    [Column("id")]
    public long Id { get; private set; }

    /// <summary>
    /// A 32-digit GUID assigned by <see cref="Create"/>, unique across <c>trax.work_queue</c>. It is
    /// the id handed back to whoever queued the work, and the job dispatcher gives the run's
    /// <c>Metadata</c> the same <c>ExternalId</c>, so the entry and its run correlate. A dispatch
    /// retry can leave several metadata rows with this id, the older ones failed.
    /// </summary>
    [Column("external_id")]
    public string ExternalId { get; set; } = null!;

    /// <summary>
    /// The fully qualified train type name to execute.
    /// </summary>
    [Column("train_name")]
    public string TrainName { get; set; } = null!;

    /// <summary>
    /// Serialized train input (JSON). Same format as Manifest.Properties.
    /// </summary>
    [Column("input")]
    public string? Input { get; set; }

    /// <summary>
    /// Fully qualified type name of the input, for deserialization.
    /// </summary>
    [Column("input_type_name")]
    public string? InputTypeName { get; set; }

    /// <summary>
    /// The current lifecycle status of this queue entry.
    /// </summary>
    [Column("status")]
    public WorkQueueStatus Status { get; set; }

    /// <summary>
    /// When this entry was created (queued).
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Identifies what this work touches, so an entry is not dispatched while another entry naming
    /// the same subject has a run in flight. Null, the default, means the entry is not serialized
    /// against anything.
    /// </summary>
    /// <remarks>
    /// Supplied by the train through <c>ServiceTrain.QueueSubjectKey</c>. It is an opaque string:
    /// Trax compares it exactly and nothing else, so its shape is the consumer's to decide. A
    /// record identity is the usual choice, because the systems that need this are the ones where
    /// two concurrent writes to one record are resolved by last-write-wins.
    ///
    /// "In flight" ends when the run reaches a terminal state, including when one of the
    /// scheduler's stale-run reapers fails it: a run left pending longer than
    /// <c>StalePendingTimeout</c>, or in progress longer than <c>StaleInProgressTimeout</c>. A run
    /// that is still working past its timeout no longer holds its subject. Only queued work is
    /// serialized; a synchronous run through the mediator does not consult the key.
    ///
    /// Only <see cref="Create"/> sets it, from <see cref="CreateWorkQueue.SubjectKey"/>, so every
    /// key goes through the checks <see cref="Create"/> makes. A Trax.Mediator older than 1.23.0
    /// set it after <see cref="Create"/> and cannot run against this version.
    /// </remarks>
    [Column("subject_key")]
    [JsonInclude]
    public string? SubjectKey { get; private set; }

    /// <summary>
    /// When this entry became eligible for dispatch, or null while it is still being staged.
    /// </summary>
    /// <remarks>
    /// Normally set at creation, so an entry is dispatchable the moment it is committed. A train
    /// that defers promotion (see <c>ServiceTrain.DeferQueuePromotion</c>) is instead committed
    /// with a null value, its <c>OnQueue</c> hook is run, and only then is this stamped in a second
    /// commit. That makes a crash between the two detectable: the entry is left unconfirmed rather
    /// than the hook's side-effect being left with no entry at all.
    ///
    /// Dispatch gates on this, so an unconfirmed entry is never claimed.
    ///
    /// Only <see cref="Create"/> sets it, which is why <see cref="Create"/> is the only way to
    /// build an entry: EF writes this column explicitly, so an entry saved with it null does not
    /// get the column's database default. It is treated as staged, is never dispatched, and is
    /// eventually cancelled by the stale-staged sweep.
    ///
    /// Entries dispatched before this column existed may also be null. Nothing reads it on a
    /// dispatched entry.
    /// </remarks>
    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// When this entry was picked up by the dispatcher.
    /// </summary>
    [Column("dispatched_at")]
    public DateTime? DispatchedAt { get; set; }

    /// <summary>
    /// The earliest time this work queue entry should be dispatched.
    /// </summary>
    /// <remarks>
    /// When set, the JobDispatcher will skip this entry until <c>ScheduledAt &lt;= now</c>.
    /// Used by <c>TriggerAsync(externalId, delay)</c> to create delayed triggers without
    /// affecting the manifest's normal schedule. Null means dispatch immediately.
    /// </remarks>
    [Column("scheduled_at")]
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// Dispatch priority for this entry. Higher values (up to 31) are dispatched first.
    /// </summary>
    [Column("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Number of times dispatch has been attempted and failed for this entry.
    /// </summary>
    /// <remarks>
    /// Incremented each time the job submitter fails to deliver the job (e.g., remote
    /// worker throttling or unavailability). When the entry is requeued after a dispatch
    /// failure, this counter is preserved so the system can stop retrying after
    /// <c>MaxDispatchAttempts</c> is reached.
    /// </remarks>
    [Column("dispatch_attempts")]
    public int DispatchAttempts { get; set; }

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Optional manifest ID — set when this entry was queued from a scheduled manifest.
    /// </summary>
    [Column("manifest_id")]
    public long? ManifestId { get; set; }

    /// <summary>
    /// The associated manifest, if this entry was queued from a manifest.
    /// </summary>
    public Manifest.Manifest? Manifest { get; set; }

    /// <summary>
    /// The metadata ID created when the dispatcher picks up this entry.
    /// </summary>
    [Column("metadata_id")]
    public long? MetadataId { get; set; }

    /// <summary>
    /// The metadata record created for this entry's execution.
    /// </summary>
    public Metadata.Metadata? Metadata { get; set; }

    /// <summary>
    /// Optional dead letter ID — set when this entry was created by requeuing a dead letter.
    /// Used by the JobDispatcher to link the retry metadata back to the dead letter.
    /// </summary>
    [Column("dead_letter_id")]
    public long? DeadLetterId { get; set; }

    /// <summary>
    /// The dead letter record that triggered this requeue, if applicable.
    /// </summary>
    public DeadLetter.DeadLetter? DeadLetter { get; set; }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new WorkQueue entry with Queued status.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="CreateWorkQueue.SubjectKey"/> is empty or whitespace, contains an unpaired surrogate, or is longer than
    /// <see cref="MaxSubjectKeyLength"/> characters.
    /// </exception>
    public static WorkQueue Create(CreateWorkQueue dto)
    {
        if (dto.SubjectKey is { } key)
            RefuseUnusableSubjectKey(key, nameof(dto));

        return new WorkQueue
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            TrainName = dto.TrainName,
            Input = dto.Input,
            InputTypeName = dto.InputTypeName,
            ManifestId = dto.ManifestId,
            Priority = Math.Clamp(dto.Priority, MinPriority, MaxPriority),
            ScheduledAt = dto.ScheduledAt,
            DeadLetterId = dto.DeadLetterId,
            Status = WorkQueueStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = dto.DeferPromotion ? null : DateTime.UtcNow,
            SubjectKey = dto.SubjectKey,
        };
    }

    // Empty or whitespace-only is refused rather than treated as a subject: every entry carrying
    // it would be serialized against every other, and it is almost always an unset identity.
    // Surrounding whitespace on a real key is kept, because the key is compared exactly.
    private static void RefuseUnusableSubjectKey(string key, string paramName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException(
                "A subject key cannot be empty or whitespace. Leave it null when the entry should "
                    + "not be serialized.",
                paramName
            );

        // Counting UTF-16 units would refuse a key of 300 emoji, which is 300 characters to
        // whoever wrote it. An unpaired surrogate is refused rather than counted: the database
        // encodes the key as UTF-8, which has no encoding for half a character, so the insert
        // would fail later and far from the caller.
        var characters = 0;
        for (var position = 0; position < key.Length; characters++)
        {
            if (
                Rune.DecodeFromUtf16(key.AsSpan(position), out _, out var consumed)
                != OperationStatus.Done
            )
                throw new ArgumentException(
                    $"A subject key cannot contain an unpaired surrogate at position {position}. "
                        + "It is half of a character, which cannot be stored; the key was likely "
                        + "cut in the middle of one.",
                    paramName
                );

            position += consumed;
        }

        if (characters > MaxSubjectKeyLength)
            throw new ArgumentException(
                $"A subject key of {characters} characters is over the limit of "
                    + $"{MaxSubjectKeyLength}. Use a record identity, or a hash of a longer one.",
                paramName
            );
    }

    /// <summary>
    /// Serializes this entry to JSON for a log line, with <see cref="Input"/> written as <c>{"_omitted": true}</c>. Not a stable format: read the
    /// properties for values.
    /// </summary>
    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            TraxLogSerialization.ForLogging(
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            )
        );

    #endregion

    /// <summary>
    /// For deserialization, EF and the EF configuration subclass only. Not public because an entry
    /// built with it leaves <see cref="ConfirmedAt"/> null and would be saved as staged and never
    /// dispatched; build a new entry with <see cref="Create"/>.
    /// </summary>
    [JsonConstructor]
    protected WorkQueue() { }
}
