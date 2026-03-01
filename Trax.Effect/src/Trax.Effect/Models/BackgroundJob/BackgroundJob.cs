using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.BackgroundJob.DTOs;

namespace Trax.Effect.Models.BackgroundJob;

/// <summary>
/// Represents a background job queued for execution by the built-in PostgreSQL task server.
/// </summary>
/// <remarks>
/// A BackgroundJob is the persistence layer for fire-and-forget job dispatch.
/// When the JobDispatcher enqueues work via <c>IBackgroundTaskServer.EnqueueAsync</c>,
/// a BackgroundJob row is inserted. Worker threads atomically claim and execute jobs,
/// then delete the row on completion (success or failure).
///
/// This replaces Hangfire's internal job queue tables with a Trax.Core-native implementation.
/// </remarks>
public class BackgroundJob : IModel
{
    #region Columns

    [Column("id")]
    public long Id { get; private set; }

    /// <summary>
    /// The ID of the Metadata record representing this job execution.
    /// </summary>
    [Column("metadata_id")]
    public long MetadataId { get; set; }

    /// <summary>
    /// Serialized workflow input (JSON), for ad-hoc executions where input is provided directly.
    /// </summary>
    [Column("input")]
    public string? Input { get; set; }

    /// <summary>
    /// Fully qualified type name of the input, for deserialization.
    /// </summary>
    [Column("input_type")]
    public string? InputType { get; set; }

    /// <summary>
    /// When this job was enqueued.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When a worker claimed this job. NULL means available for dequeue.
    /// A stale value (older than VisibilityTimeout) makes the job eligible for re-claim.
    /// </summary>
    [Column("fetched_at")]
    public DateTime? FetchedAt { get; set; }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new BackgroundJob ready for enqueue.
    /// </summary>
    public static BackgroundJob Create(CreateBackgroundJob dto)
    {
        return new BackgroundJob
        {
            MetadataId = dto.MetadataId,
            Input = dto.Input,
            InputType = dto.InputType,
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
    public BackgroundJob() { }
}
