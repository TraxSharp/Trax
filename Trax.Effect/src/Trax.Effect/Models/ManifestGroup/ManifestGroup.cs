using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.ManifestGroup;

/// <summary>
/// Represents a logical group of manifests with shared dispatch settings.
/// </summary>
/// <remarks>
/// ManifestGroup provides per-group controls for dispatch behavior:
/// - <see cref="MaxActiveJobs"/>: limits concurrent executions within the group
/// - <see cref="Priority"/>: determines dispatch ordering between groups
/// - <see cref="IsEnabled"/>: enables/disables all manifests in the group
///
/// Every manifest belongs to exactly one ManifestGroup. Groups are auto-created
/// during scheduling if they don't already exist.
/// </remarks>
public class ManifestGroup : IModel
{
    /// <summary>
    /// Database-generated primary key, referenced by <c>Manifest.ManifestGroupId</c>. Zero until
    /// the group is inserted.
    /// </summary>
    [Column("id")]
    public long Id { get; }

    /// <summary>
    /// Gets or sets the unique name for this group.
    /// </summary>
    [Column("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the maximum number of concurrent active jobs for this group.
    /// Null means no per-group limit (only the global MaxActiveJobs applies).
    /// </summary>
    [Column("max_active_jobs")]
    public int? MaxActiveJobs { get; set; }

    /// <summary>
    /// Gets or sets the dispatch priority for this group (0-31).
    /// Higher-priority groups have their work queue entries dispatched first.
    /// </summary>
    [Column("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets whether manifests in this group are eligible for dispatch.
    /// When false, no manifests in this group will be queued or dispatched.
    /// </summary>
    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// UTC time the group was created. Set by the code that inserts it (the scheduler stamps it when
    /// it creates a group while scheduling); no database trigger maintains it.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC time of the last change, stamped by the scheduler when it re-schedules into the group
    /// and by the operations that edit a group's settings. No database trigger maintains it, so a
    /// direct write through <c>IDataContext</c> must set it itself.
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets the collection of manifests belonging to this group.
    /// </summary>
    public ICollection<Manifest.Manifest> Manifests { get; private set; } = [];

    /// <summary>
    /// Serializes this group to JSON for a log line. Not a stable format: read the
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

    /// <summary>
    /// For JSON deserialization, EF Core materialization, and creating a group directly. A new
    /// group is enabled with priority 0 and no per-group job limit.
    /// </summary>
    [JsonConstructor]
    public ManifestGroup() { }
}
