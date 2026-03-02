using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Services.BackgroundTaskServer;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Sets the polling interval for both ManifestManager and JobDispatcher.
    /// For independent control, use <see cref="ManifestManagerPollingInterval"/> and <see cref="JobDispatcherPollingInterval"/>.
    /// </summary>
    /// <param name="interval">The polling interval (default: 5 seconds)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder PollingInterval(TimeSpan interval)
    {
        _configuration.ManifestManagerPollingInterval = interval;
        _configuration.JobDispatcherPollingInterval = interval;
        return this;
    }

    /// <summary>
    /// Sets the interval at which ManifestManagerPollingService evaluates manifests and writes to the work queue.
    /// </summary>
    /// <param name="interval">The polling interval (default: 5 seconds)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder ManifestManagerPollingInterval(TimeSpan interval)
    {
        _configuration.ManifestManagerPollingInterval = interval;
        return this;
    }

    /// <summary>
    /// Sets the interval at which JobDispatcherPollingService reads the work queue and dispatches jobs.
    /// </summary>
    /// <param name="interval">The polling interval (default: 5 seconds)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder JobDispatcherPollingInterval(TimeSpan interval)
    {
        _configuration.JobDispatcherPollingInterval = interval;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of active jobs (Pending + InProgress) allowed across all manifests.
    /// </summary>
    /// <param name="maxJobs">The maximum active jobs (default: 100, null = unlimited)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// When the total number of active jobs reaches this limit, no new jobs will be enqueued
    /// until existing jobs complete.
    /// </remarks>
    public SchedulerConfigurationBuilder MaxActiveJobs(int? maxJobs)
    {
        _configuration.MaxActiveJobs = maxJobs;
        return this;
    }

    /// <summary>
    /// Excludes a train type from the MaxActiveJobs count.
    /// </summary>
    /// <typeparam name="TTrain">The train class type to exclude</typeparam>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Internal scheduler trains are excluded by default. Use this method to
    /// exclude additional train types whose Metadata should not count toward the limit.
    /// </remarks>
    public SchedulerConfigurationBuilder ExcludeFromMaxActiveJobs<TTrain>()
        where TTrain : class
    {
        _configuration.ExcludedTrainTypeNames.Add(typeof(TTrain).FullName!);
        return this;
    }

    /// <summary>
    /// Sets the priority boost automatically applied to dependent train work queue entries.
    /// </summary>
    /// <param name="boost">The priority boost (default: 16, range: 0-31)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DependentPriorityBoost(int boost)
    {
        _configuration.DependentPriorityBoost = Math.Clamp(
            boost,
            WorkQueue.MinPriority,
            WorkQueue.MaxPriority
        );
        return this;
    }

    /// <summary>
    /// Sets the default number of retry attempts before a job is dead-lettered.
    /// </summary>
    /// <param name="maxRetries">The maximum retry count (default: 3)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultMaxRetries(int maxRetries)
    {
        _configuration.DefaultMaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Sets the default delay between retry attempts.
    /// </summary>
    /// <param name="delay">The retry delay (default: 5 minutes)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultRetryDelay(TimeSpan delay)
    {
        _configuration.DefaultRetryDelay = delay;
        return this;
    }

    /// <summary>
    /// Sets the multiplier applied to retry delay on each subsequent retry.
    /// </summary>
    /// <param name="multiplier">The backoff multiplier (default: 2.0)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder RetryBackoffMultiplier(double multiplier)
    {
        _configuration.RetryBackoffMultiplier = multiplier;
        return this;
    }

    /// <summary>
    /// Sets the maximum retry delay to prevent unbounded backoff growth.
    /// </summary>
    /// <param name="maxDelay">The maximum delay (default: 1 hour)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder MaxRetryDelay(TimeSpan maxDelay)
    {
        _configuration.MaxRetryDelay = maxDelay;
        return this;
    }

    /// <summary>
    /// Sets the timeout after which a running job is considered stuck.
    /// </summary>
    /// <param name="timeout">The job timeout (default: 1 hour)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultJobTimeout(TimeSpan timeout)
    {
        _configuration.DefaultJobTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the default misfire policy for manifests that do not specify one.
    /// </summary>
    /// <param name="policy">The default misfire policy (default: FireOnceNow)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultMisfirePolicy(MisfirePolicy policy)
    {
        _configuration.DefaultMisfirePolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the default misfire threshold — the grace period before misfire policies take effect.
    /// </summary>
    /// <param name="threshold">The misfire threshold (default: 60 seconds)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultMisfireThreshold(TimeSpan threshold)
    {
        _configuration.DefaultMisfireThreshold = threshold;
        return this;
    }

    /// <summary>
    /// Sets whether to automatically recover stuck jobs on scheduler startup.
    /// </summary>
    /// <param name="recover">True to recover stuck jobs (default: true)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder RecoverStuckJobsOnStartup(bool recover = true)
    {
        _configuration.RecoverStuckJobsOnStartup = recover;
        return this;
    }

    /// <summary>
    /// Uses the in-memory task server for testing and development.
    /// </summary>
    /// <remarks>
    /// The in-memory task server executes jobs immediately and synchronously.
    /// Useful for unit/integration testing without external infrastructure.
    /// </remarks>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseInMemoryTaskServer()
    {
        _taskServerRegistration = services =>
        {
            services.AddScoped<IBackgroundTaskServer, InMemoryTaskServer>();
        };
        return this;
    }

    /// <summary>
    /// Uses the built-in PostgreSQL task server for background job execution.
    /// </summary>
    /// <remarks>
    /// Jobs are persisted to the <c>trax.background_job</c> table and executed by
    /// concurrent worker tasks using PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c> for atomic dequeue.
    /// Requires <c>AddPostgresEffect(connectionString)</c> to have been called first.
    /// </remarks>
    /// <param name="configure">Optional configuration for worker count, polling interval, and timeouts</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UsePostgresTaskServer(
        Action<PostgresTaskServerOptions>? configure = null
    )
    {
        _taskServerRegistration = services =>
        {
            var options = new PostgresTaskServerOptions();
            configure?.Invoke(options);
            services.AddSingleton(options);
            services.AddScoped<IBackgroundTaskServer, PostgresTaskServer>();
            services.AddHostedService<Scheduler.Services.PostgresWorkerService.PostgresWorkerService>();
        };
        return this;
    }

    /// <summary>
    /// Registers a custom background task server registration action.
    /// </summary>
    /// <remarks>
    /// This is used by task server implementations (Hangfire, Quartz, etc.) to register
    /// their services. Most users should use the specific extension methods like UsePostgresTaskServer().
    /// </remarks>
    /// <param name="registration">The action to register task server services</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseTaskServer(Action<IServiceCollection> registration)
    {
        _taskServerRegistration = registration;
        return this;
    }

    /// <summary>
    /// Sets whether to automatically prune orphaned manifests on startup.
    /// </summary>
    /// <param name="prune">True to prune orphaned manifests (default: true)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// When enabled, manifests in the database that are not defined in the startup
    /// configuration are deleted along with their related data. Disable this if you
    /// create manifests dynamically at runtime.
    /// </remarks>
    public SchedulerConfigurationBuilder PruneOrphanedManifests(bool prune = true)
    {
        _configuration.PruneOrphanedManifests = prune;
        return this;
    }
}
