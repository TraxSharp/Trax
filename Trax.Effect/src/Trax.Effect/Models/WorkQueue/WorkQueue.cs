using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;

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
    public const int MinPriority = 0;
    public const int MaxPriority = 31;

    #region Columns

    [Column("id")]
    public long Id { get; private set; }

    [Column("external_id")]
    public string ExternalId { get; set; }

    /// <summary>
    /// The fully qualified train type name to execute.
    /// </summary>
    [Column("train_name")]
    public string TrainName { get; set; }

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
    /// When this entry was picked up by the dispatcher.
    /// </summary>
    [Column("dispatched_at")]
    public DateTime? DispatchedAt { get; set; }

    /// <summary>
    /// Dispatch priority for this entry. Higher values (up to 31) are dispatched first.
    /// </summary>
    [Column("priority")]
    public int Priority { get; set; }

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

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new WorkQueue entry with Queued status.
    /// </summary>
    public static WorkQueue Create(CreateWorkQueue dto)
    {
        return new WorkQueue
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            TrainName = dto.TrainName,
            Input = dto.Input,
            InputTypeName = dto.InputTypeName,
            ManifestId = dto.ManifestId,
            Priority = Math.Clamp(dto.Priority, MinPriority, MaxPriority),
            Status = WorkQueueStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    #endregion

    [JsonConstructor]
    public WorkQueue() { }
}
