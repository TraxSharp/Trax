using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;

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

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets the collection of manifests belonging to this group.
    /// </summary>
    public ICollection<Manifest.Manifest> Manifests { get; private set; } = [];

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            Trax.CoreEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    [JsonConstructor]
    public ManifestGroup() { }
}
