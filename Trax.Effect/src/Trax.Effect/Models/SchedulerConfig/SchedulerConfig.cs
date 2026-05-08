using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.TraxEffectConfiguration;

namespace Trax.Effect.Models.SchedulerConfig;

/// <summary>
/// Persisted scheduler runtime settings. Holds the dashboard-editable subset of
/// <c>SchedulerConfiguration</c> + <c>LocalWorkerOptions</c> + <c>MetadataCleanupConfiguration</c>
/// so a React or Blazor dashboard can read and update them, and so settings survive
/// app restarts.
/// </summary>
/// <remarks>
/// This is a singleton table: exactly one row exists, with <see cref="Id"/> always 1.
/// A CHECK constraint (set in the migration) prevents inserts of any other id.
/// </remarks>
public class SchedulerConfig : IModel
{
    /// <summary>The singleton row id. Always 1.</summary>
    public const long SingletonId = 1L;

    [Column("id")]
    public long Id { get; set; } = SingletonId;

    [Column("manifest_manager_enabled")]
    public bool ManifestManagerEnabled { get; set; } = true;

    [Column("job_dispatcher_enabled")]
    public bool JobDispatcherEnabled { get; set; } = true;

    [Column("manifest_manager_polling_interval")]
    public TimeSpan ManifestManagerPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Column("job_dispatcher_polling_interval")]
    public TimeSpan JobDispatcherPollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    [Column("max_active_jobs")]
    public int? MaxActiveJobs { get; set; } = 10;

    [Column("default_max_retries")]
    public int DefaultMaxRetries { get; set; } = 3;

    [Column("default_retry_delay")]
    public TimeSpan DefaultRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    [Column("retry_backoff_multiplier")]
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    [Column("max_retry_delay")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    [Column("default_job_timeout")]
    public TimeSpan DefaultJobTimeout { get; set; } = TimeSpan.FromMinutes(20);

    [Column("stale_pending_timeout")]
    public TimeSpan StalePendingTimeout { get; set; } = TimeSpan.FromMinutes(20);

    [Column("recover_stuck_jobs_on_startup")]
    public bool RecoverStuckJobsOnStartup { get; set; } = true;

    [Column("dead_letter_retention_period")]
    public TimeSpan DeadLetterRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    [Column("auto_purge_dead_letters")]
    public bool AutoPurgeDeadLetters { get; set; } = true;

    [Column("local_worker_count")]
    public int? LocalWorkerCount { get; set; }

    [Column("metadata_cleanup_interval")]
    public TimeSpan? MetadataCleanupInterval { get; set; }

    [Column("metadata_cleanup_retention")]
    public TimeSpan? MetadataCleanupRetention { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public override string ToString() =>
        JsonSerializer.Serialize(this, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    [JsonConstructor]
    public SchedulerConfig() { }
}
