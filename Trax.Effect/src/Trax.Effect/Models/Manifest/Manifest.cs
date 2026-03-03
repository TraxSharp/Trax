using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LanguageExt;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.Manifest;

/// <summary>
/// Represents a job definition that describes what train to run, how to schedule it,
/// and what retry policies to apply.
/// </summary>
/// <remarks>
/// A Manifest is the "job definition" in the scheduling system. It defines:
/// - Which train to execute (via <see cref="Name"/>)
/// - Default configuration/properties for the train
/// - Scheduling rules (cron, interval, or manual-only)
/// - Retry and timeout policies
///
/// Each execution of a Manifest creates a new <see cref="Metadata.Metadata"/> record.
/// This allows for full audit trail of every execution attempt.
/// </remarks>
public class Manifest : IModel
{
    #region Columns

    [Column("id")]
    public long Id { get; }

    [Column("external_id")]
    public string ExternalId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("property_type")]
    public string? PropertyTypeName { get; set; }

    [Column("properties")]
    public string? Properties { get; set; }

    [NotMapped]
    [JsonIgnore]
    public Type PropertyType =>
        PropertyTypeName == null ? typeof(Unit) : ResolveType(PropertyTypeName);

    [NotMapped]
    [JsonIgnore]
    public Type NameType => Name == null ? typeof(Unit) : ResolveType(Name);

    #region Scheduling Properties

    /// <summary>
    /// Gets or sets whether this manifest is enabled for scheduling.
    /// </summary>
    /// <remarks>
    /// When false, the ManifestManager will skip this manifest during polling.
    /// This allows pausing jobs without deleting them.
    /// </remarks>
    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the scheduling strategy for this manifest.
    /// </summary>
    [Column("schedule_type")]
    public ScheduleType ScheduleType { get; set; } = ScheduleType.None;

    /// <summary>
    /// Gets or sets the cron expression for Cron-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ScheduleType"/> is <see cref="ScheduleType.Cron"/>.
    /// Uses standard cron format (e.g., "0 3 * * *" for daily at 3am).
    /// </remarks>
    [Column("cron_expression")]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets the interval in seconds for Interval-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="ScheduleType"/> is <see cref="ScheduleType.Interval"/>.
    /// </remarks>
    [Column("interval_seconds")]
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before dead-lettering.
    /// </summary>
    /// <remarks>
    /// Each retry creates a new Metadata record. After this many failed attempts,
    /// the job is moved to the dead letter queue for manual intervention.
    /// </remarks>
    [Column("max_retries")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the timeout in seconds for job execution.
    /// </summary>
    /// <remarks>
    /// If a job is in "InProgress" state for longer than this duration,
    /// it may be considered stuck and subject to recovery logic.
    /// Null means use the global default from SchedulerConfiguration.
    /// </remarks>
    [Column("timeout_seconds")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last successful execution.
    /// </summary>
    /// <remarks>
    /// Updated automatically when a job completes successfully.
    /// Useful for scheduling decisions and "delta mode" trains that
    /// need to know when data was last synchronized.
    /// </remarks>
    [Column("last_successful_run")]
    public DateTime? LastSuccessfulRun { get; set; }

    /// <summary>
    /// Gets or sets the ID of the ManifestGroup this manifest belongs to.
    /// </summary>
    /// <remarks>
    /// Every manifest must belong to a ManifestGroup. Groups provide per-group dispatch
    /// controls (MaxActiveJobs, Priority, IsEnabled) and dashboard grouping.
    /// </remarks>
    [Column("manifest_group_id")]
    public long ManifestGroupId { get; set; }

    /// <summary>
    /// Gets or sets the ID of the parent manifest that this manifest depends on.
    /// </summary>
    /// <remarks>
    /// When set, this manifest will only be queued for execution after the parent manifest
    /// completes successfully. The <see cref="ScheduleType"/> should be set to
    /// <see cref="Enums.ScheduleType.Dependent"/> when this property is used.
    /// </remarks>
    [Column("depends_on_manifest_id")]
    public long? DependsOnManifestId { get; set; }

    /// <summary>
    /// Gets or sets the default dispatch priority for work queue entries created from this manifest.
    /// </summary>
    /// <remarks>
    /// Values range from 0 (lowest) to 31 (highest). Higher-priority entries are dispatched
    /// before lower-priority ones. This value is denormalized onto WorkQueue entries at creation time.
    /// </remarks>
    [Column("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the misfire policy for this manifest.
    /// </summary>
    /// <remarks>
    /// Determines behavior when a scheduled run is missed (e.g., scheduler was down).
    /// Only applies to Cron and Interval schedule types.
    /// </remarks>
    [Column("misfire_policy")]
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.FireOnceNow;

    /// <summary>
    /// Gets or sets the misfire threshold in seconds for this manifest.
    /// </summary>
    /// <remarks>
    /// Defines the grace period for misfire detection. If a manifest is overdue by less than
    /// this threshold, it fires normally regardless of <see cref="MisfirePolicy"/>.
    /// Null means use the global default from SchedulerConfiguration.DefaultMisfireThreshold.
    /// </remarks>
    [Column("misfire_threshold_seconds")]
    public int? MisfireThresholdSeconds { get; set; }

    /// <summary>
    /// Gets or sets the earliest time this manifest should be executed.
    /// </summary>
    /// <remarks>
    /// Used with <see cref="ScheduleType.Once"/> to define when the one-off job should fire.
    /// The ManifestManager will queue a work queue entry when <c>ScheduledAt &lt;= now</c>
    /// and <see cref="LastSuccessfulRun"/> is null. After successful execution, the manifest
    /// is automatically disabled.
    /// </remarks>
    [Column("scheduled_at")]
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets the JSON-serialized exclusion windows for this manifest.
    /// </summary>
    /// <remarks>
    /// Stored as a JSONB column containing an array of <see cref="Exclusion"/> objects.
    /// When any exclusion matches the current time, the scheduling logic treats the period
    /// as "intentionally skipped" (not a misfire). Null or empty means no exclusions.
    /// </remarks>
    [Column("exclusions")]
    public string? Exclusions { get; set; }

    #endregion

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Gets or sets the ManifestGroup this manifest belongs to.
    /// </summary>
    public ManifestGroup.ManifestGroup ManifestGroup { get; set; } = null!;

    /// <summary>
    /// Gets or sets the parent manifest that this manifest depends on.
    /// </summary>
    public Manifest? DependsOnManifest { get; set; }

    /// <summary>
    /// Gets the collection of metadata records (train executions) associated with this manifest.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal from a job definition (Manifest)
    /// to all its execution records (Metadata). It is populated by the ORM when loaded
    /// from the database.
    /// </remarks>
    public ICollection<Metadata.Metadata> Metadatas { get; private set; } = [];

    /// <summary>
    /// Gets the collection of dead letter records for jobs that exceeded retry limits.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal from a job definition (Manifest)
    /// to all its dead-lettered executions. It is populated by the ORM when loaded
    /// from the database.
    /// </remarks>
    public ICollection<DeadLetter.DeadLetter> DeadLetters { get; private set; } = [];

    public ICollection<WorkQueue.WorkQueue> WorkQueues { get; private set; } = [];

    #endregion

    #region Functions

    /// <summary>
    /// Deserializes the exclusions JSON into a list of <see cref="Exclusion"/> objects.
    /// </summary>
    public List<Exclusion> GetExclusions()
    {
        if (string.IsNullOrEmpty(Exclusions))
            return [];

        return JsonSerializer.Deserialize<List<Exclusion>>(
                Exclusions,
                TraxJsonSerializationOptions.ManifestProperties
            ) ?? [];
    }

    /// <summary>
    /// Serializes the given exclusions and stores them as JSON.
    /// </summary>
    public void SetExclusions(List<Exclusion> exclusions)
    {
        Exclusions =
            exclusions.Count == 0
                ? null
                : JsonSerializer.Serialize(
                    exclusions,
                    TraxJsonSerializationOptions.ManifestProperties
                );
    }

    public static Manifest Create(CreateManifest manifest)
    {
        if (manifest.Name.FullName is null)
            throw new Exception($"Could not get a full name from ({manifest.Name})");

        var newManifest = new Manifest()
        {
            Name = manifest.Name.FullName,
            ExternalId = Guid.NewGuid().ToString("N"),
            // Scheduling properties
            IsEnabled = manifest.IsEnabled,
            ScheduleType = manifest.ScheduleType,
            CronExpression = manifest.CronExpression,
            IntervalSeconds = manifest.IntervalSeconds,
            MaxRetries = manifest.MaxRetries,
            TimeoutSeconds = manifest.TimeoutSeconds,
            DependsOnManifestId = manifest.DependsOnManifestId,
            Priority = manifest.Priority,
            MisfirePolicy = manifest.MisfirePolicy,
            MisfireThresholdSeconds = manifest.MisfireThresholdSeconds,
            ScheduledAt = manifest.ScheduledAt,
            Exclusions = manifest.Exclusions,
        };

        if (manifest.Properties != null)
            newManifest.SetProperties(manifest.Properties);

        return newManifest;
    }

    public Unit SetProperties(IManifestProperties properties)
    {
        var propertiesType = properties.GetType();

        PropertyTypeName = propertiesType.FullName;

        var json = JsonSerializer.Serialize(
            properties,
            propertiesType,
            TraxJsonSerializationOptions.ManifestProperties
        );

        var node = JsonNode.Parse(json);
        if (node is JsonObject obj)
        {
            var reordered = new JsonObject { ["$type"] = propertiesType.FullName };
            foreach (var kvp in obj)
                reordered[kvp.Key] = kvp.Value?.DeepClone();
            Properties = reordered.ToJsonString(TraxJsonSerializationOptions.ManifestProperties);
        }
        else
        {
            Properties = json;
        }

        return Unit.Default;
    }

    public TProperty GetProperties<TProperty>()
        where TProperty : IManifestProperties => (TProperty)GetProperties(typeof(TProperty));

    public object GetProperties(Type propertyType)
    {
        if (propertyType != PropertyType)
            throw new Exception($"Passed type ({propertyType}) is not saved type ({PropertyType})");

        if (string.IsNullOrEmpty(Properties))
            throw new Exception(
                $"Cannot deserialize null property object with type ({PropertyType})"
            );

        return JsonSerializer.Deserialize(
                Properties,
                propertyType,
                TraxJsonSerializationOptions.ManifestProperties
            )
            ?? throw new Exception(
                $"Could not deserialize property object ({Properties}) with type ({PropertyType})"
            );
    }

    /// <summary>
    /// Deserializes the Properties JSON using the type resolved from <see cref="PropertyTypeName"/>.
    /// </summary>
    public object GetPropertiesUntyped()
    {
        if (string.IsNullOrEmpty(Properties))
            throw new Exception(
                $"Cannot deserialize null property object with type ({PropertyType})"
            );

        return JsonSerializer.Deserialize(
                Properties,
                PropertyType,
                TraxJsonSerializationOptions.ManifestProperties
            )
            ?? throw new Exception(
                $"Could not deserialize property object ({Properties}) with type ({PropertyType})"
            );
    }

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    #endregion

    [JsonConstructor]
    public Manifest() { }

    /// <summary>
    /// Resolves a type by its full name, searching all loaded assemblies.
    /// </summary>
    private static Type ResolveType(string typeName)
    {
        // First try the standard Type.GetType which works for types in the current assembly and mscorlib
        var type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Search through all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null)
                return type;
        }

        throw new TypeLoadException($"Unable to find type: {typeName}");
    }
}
