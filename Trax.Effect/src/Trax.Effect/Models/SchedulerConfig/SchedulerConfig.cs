using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.SchedulerConfig;

/// <summary>
/// Persisted scheduler runtime settings. Holds the dashboard-editable subset of
/// <c>SchedulerConfiguration</c> + <c>LocalWorkerOptions</c> + <c>MetadataCleanupConfiguration</c>
/// so a React or Blazor dashboard can read and update them, and so settings survive
/// app restarts.
/// </summary>
/// <remarks>
/// <para>
/// This is a singleton table: at most one row exists, with <see cref="Id"/> always 1. A CHECK
/// constraint (set in the migration) prevents inserts of any other id. There is no row until an
/// operator first changes a setting through the dashboard or the GraphQL operations.
/// </para>
/// <para>
/// <see cref="Overrides"/> says which settings a save named. Those win over the settings
/// configured in code; the rest keep each host's code value. A row written before that column
/// existed has it null, and all of its columns were written as chosen values. Each property below
/// is the persisted form of the scheduler setting of the same name; the defaults here match the
/// scheduler's builder defaults.
/// </para>
/// </remarks>
public class SchedulerConfig : IModel
{
    /// <summary>The singleton row id. Always 1.</summary>
    public const long SingletonId = 1L;

    /// <summary>
    /// The row's key. Always <see cref="SingletonId"/>; the database refuses any other value.
    /// </summary>
    [Column("id")]
    public long Id { get; set; } = SingletonId;

    /// <summary>
    /// Whether the manifest manager runs on its polling cycle. When false, scheduled manifests
    /// stop producing work queue entries; entries already queued are unaffected.
    /// </summary>
    [Column("manifest_manager_enabled")]
    public bool ManifestManagerEnabled { get; set; } = true;

    /// <summary>
    /// Whether the job dispatcher runs on its polling cycle. When false, nothing is dispatched and
    /// work queue entries accumulate.
    /// </summary>
    [Column("job_dispatcher_enabled")]
    public bool JobDispatcherEnabled { get; set; } = true;

    /// <summary>How often the manifest manager polls for due manifests. Default 5 seconds.</summary>
    [Column("manifest_manager_polling_interval")]
    public TimeSpan ManifestManagerPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the job dispatcher polls the work queue. Default 2 seconds.</summary>
    [Column("job_dispatcher_polling_interval")]
    public TimeSpan JobDispatcherPollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The most Pending and InProgress runs allowed across all manifests before the dispatcher
    /// holds further entries in the queue; the scheduler's own internal trains do not count. Null
    /// means no limit. Defaults to 10 here, although the column's SQL default is null.
    /// </summary>
    [Column("max_active_jobs")]
    public int? MaxActiveJobs { get; set; } = 10;

    /// <summary>
    /// Intended as the retry limit for manifests that set none. Stored and editable, but the
    /// scheduler does not read it today: each manifest's own <c>MaxRetries</c> (default 3) decides
    /// when it is dead-lettered. Default 3.
    /// </summary>
    [Column("default_max_retries")]
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay before the first retry of a failed run; later retries multiply it by
    /// <see cref="RetryBackoffMultiplier"/>. Default 5 minutes.
    /// </summary>
    [Column("default_retry_delay")]
    public TimeSpan DefaultRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Factor applied to the retry delay on each subsequent retry. 1.0 keeps the delay constant;
    /// the default 2.0 doubles it, capped at <see cref="MaxRetryDelay"/>.
    /// </summary>
    [Column("retry_backoff_multiplier")]
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>Upper bound on the backed-off retry delay. Default 1 hour.</summary>
    [Column("max_retry_delay")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a run may stay InProgress before the scheduler treats it as stuck and cancels it,
    /// when its manifest sets no <c>TimeoutSeconds</c>. Default 20 minutes.
    /// </summary>
    [Column("default_job_timeout")]
    public TimeSpan DefaultJobTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// How long a run may stay Pending (dispatched but never picked up) before the manifest manager
    /// marks it Failed. Default 20 minutes.
    /// </summary>
    [Column("stale_pending_timeout")]
    public TimeSpan StalePendingTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Whether a starting scheduler marks every InProgress run that began before it started as
    /// Failed. This covers runs on every host sharing the database, not only this one. Default
    /// true.
    /// </summary>
    [Column("recover_stuck_jobs_on_startup")]
    public bool RecoverStuckJobsOnStartup { get; set; } = true;

    /// <summary>
    /// How long a resolved dead letter is kept before the automatic purge deletes it. Default 30
    /// days.
    /// </summary>
    [Column("dead_letter_retention_period")]
    public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether resolved dead letters older than <see cref="DeadLetterRetentionPeriod"/> are deleted
    /// automatically. Default true.
    /// </summary>
    [Column("auto_purge_dead_letters")]
    public bool AutoPurgeDeadLetters { get; set; } = true;

    /// <summary>
    /// Number of local worker tasks that execute background jobs. Null when the host has no local
    /// workers registered, or leaves the configured count (by default the processor count) in
    /// place.
    /// </summary>
    [Column("local_worker_count")]
    public int? LocalWorkerCount { get; set; }

    /// <summary>
    /// How often the metadata cleanup runs. Null when metadata cleanup is not enabled, and a null
    /// value leaves the configured interval in place.
    /// </summary>
    [Column("metadata_cleanup_interval")]
    public TimeSpan? MetadataCleanupInterval { get; set; }

    /// <summary>
    /// How long finished runs are kept before metadata cleanup deletes them. Replaces the default
    /// retention only; a per-train retention set in code is unaffected. Null when metadata cleanup
    /// is not enabled, and a null value leaves the configured retention in place.
    /// </summary>
    [Column("metadata_cleanup_retention")]
    public TimeSpan? MetadataCleanupRetention { get; set; }

    /// <summary>
    /// UTC time the row was last written, stamped by the operation that persisted it.
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The settings a save named, as a JSON object of setting name to stored value, or null for a
    /// row written before the column existed.
    /// </summary>
    /// <remarks>
    /// A row holds one column per setting, so on its own it cannot tell a value an operator chose
    /// from one that was only the saving host's current value, and a save from one host would pin
    /// every setting to that host's values. This records which settings a save actually named: a
    /// setting named here is the operator's, and every other setting keeps the value each host
    /// configures in code. A setting with no column of its own is stored only here.
    ///
    /// Null means the row predates the column and every column holds a chosen value, as every save
    /// used to write them all. An empty object means no setting is overridden. Read and write it
    /// through <see cref="TryGetOverride{T}"/>, <see cref="SetOverride{T}"/> and
    /// <see cref="RemoveOverride"/>, which keep it a JSON object; removing a key is how a setting
    /// returns to its code value.
    /// </remarks>
    [Column("overrides")]
    public string? Overrides { get; set; }

    /// <summary>
    /// Reads the override stored for <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The setting's name, compared exactly.</param>
    /// <param name="value">The stored value, or <c>default</c> when there is none.</param>
    /// <returns>
    /// True when <see cref="Overrides"/> names the setting. False when it does not, including when
    /// <see cref="Overrides"/> is null: a legacy row's columns are read directly, not through this.
    /// </returns>
    /// <exception cref="JsonException">The stored value cannot be read as <typeparamref name="T"/>.</exception>
    public bool TryGetOverride<T>(string name, out T? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ReadOverrides() is { } overrides && overrides.TryGetPropertyValue(name, out var node))
        {
            value = node is null ? default : node.Deserialize<T>(OverrideSerialization);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Records that a save named <paramref name="name"/>, with <paramref name="value"/> as its
    /// value. A null <see cref="Overrides"/> becomes an object holding just this setting.
    /// </summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="value">The value to store; null is stored as JSON null, and still counts as set.</param>
    public void SetOverride<T>(string name, T value)
    {
        ArgumentNullException.ThrowIfNull(name);

        var overrides = ReadOverrides() ?? [];
        overrides[name] = JsonSerializer.SerializeToNode(value, OverrideSerialization);
        Overrides = overrides.ToJsonString(OverrideSerialization);
    }

    /// <summary>
    /// Removes the override for <paramref name="name"/>, so the setting returns to the value each
    /// host configures in code.
    /// </summary>
    /// <returns>True when there was an override to remove.</returns>
    public bool RemoveOverride(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ReadOverrides() is not { } overrides || !overrides.Remove(name))
            return false;

        Overrides = overrides.ToJsonString(OverrideSerialization);
        return true;
    }

    /// <summary>
    /// The names <see cref="Overrides"/> holds, or an empty list when it is null or empty.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public IReadOnlyList<string> OverriddenSettings =>
        ReadOverrides()?.Select(p => p.Key).ToList() ?? [];

    private static readonly JsonSerializerOptions OverrideSerialization = new(
        JsonSerializerDefaults.General
    );

    private JsonObject? ReadOverrides() =>
        string.IsNullOrEmpty(Overrides)
            ? null
            : JsonNode.Parse(Overrides) as JsonObject
                ?? throw new JsonException(
                    "scheduler_config.overrides must hold a JSON object of setting name to value"
                );

    /// <summary>
    /// Serializes this settings row to JSON for a log line. Not a stable format: read the
    /// properties for values.
    /// </summary>
    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            TraxLogSerialization.ForLogging(
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            )
        );

    /// <summary>
    /// Creates a row holding the scheduler's builder defaults, with <see cref="Id"/> set to
    /// <see cref="SingletonId"/>. Also used by JSON deserialization and EF Core.
    /// </summary>
    [JsonConstructor]
    public SchedulerConfig() { }
}
