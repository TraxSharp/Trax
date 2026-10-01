using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Services.RunExecutor;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Sets the polling interval for both ManifestManager and JobDispatcher.
    /// For independent control, use <see cref="ManifestManagerPollingInterval"/> and <see cref="JobDispatcherPollingInterval"/>.
    /// </summary>
    /// <param name="interval">
    /// The polling interval (default: 5 seconds for the ManifestManager, 2 for the JobDispatcher).
    /// Must be between one second and 30 days; the scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder PollingInterval(TimeSpan interval)
    {
        _configuration.ManifestManagerPollingInterval = interval;
        _configuration.JobDispatcherPollingInterval = interval;
        _manifestManagerIntervalSetBy = nameof(PollingInterval);
        _jobDispatcherIntervalSetBy = nameof(PollingInterval);
        return this;
    }

    /// <summary>
    /// Sets the interval at which ManifestManagerPollingService evaluates manifests and writes to the work queue.
    /// </summary>
    /// <param name="interval">
    /// The polling interval (default: 5 seconds). Must be between one second and 30 days; the
    /// scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder ManifestManagerPollingInterval(TimeSpan interval)
    {
        _configuration.ManifestManagerPollingInterval = interval;
        _manifestManagerIntervalSetBy = nameof(ManifestManagerPollingInterval);
        return this;
    }

    /// <summary>
    /// Sets the interval at which JobDispatcherPollingService reads the work queue and dispatches jobs.
    /// </summary>
    /// <param name="interval">
    /// The polling interval (default: 2 seconds). Must be between one second and 30 days; the
    /// scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder JobDispatcherPollingInterval(TimeSpan interval)
    {
        _configuration.JobDispatcherPollingInterval = interval;
        _jobDispatcherIntervalSetBy = nameof(JobDispatcherPollingInterval);
        return this;
    }

    /// <summary>
    /// Sets how long the JobDispatcher may go without completing a cycle before the
    /// <c>AddTraxSchedulerLiveness()</c> health check reports unhealthy.
    /// </summary>
    /// <param name="threshold">
    /// The staleness threshold (default: max(JobDispatcherPollingInterval * 10, 30s)). Must be
    /// between one second and ten years; the scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder SchedulerLivenessThreshold(TimeSpan threshold)
    {
        _configuration.SchedulerLivenessThreshold = threshold;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of work queue entries dispatched concurrently per polling cycle.
    /// </summary>
    /// <param name="maxConcurrent">The concurrency limit (minimum: 1, default: 1)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Useful when using <see cref="UseRemoteWorkers"/> where each dispatch blocks on an
    /// HTTP POST until the remote endpoint completes. For local workers, this has minimal impact.
    /// </remarks>
    public SchedulerConfigurationBuilder MaxConcurrentDispatch(int maxConcurrent)
    {
        _configuration.MaxConcurrentDispatch = Math.Max(1, maxConcurrent);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of dispatch attempts before a work queue entry is permanently failed.
    /// </summary>
    /// <param name="maxAttempts">The maximum attempts (default: 5, 0 = fail immediately on first dispatch failure)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// When the job submitter fails (e.g., remote worker throttling after retry exhaustion),
    /// the work queue entry is requeued for the next dispatcher cycle. After this many total
    /// failed attempts, the entry stays in Dispatched status and the dead letter mechanism
    /// handles it. Set to 0 to disable requeuing (preserves pre-1.2.0 behavior).
    /// </remarks>
    public SchedulerConfigurationBuilder MaxDispatchAttempts(int maxAttempts)
    {
        _configuration.MaxDispatchAttempts = Math.Max(0, maxAttempts);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of active jobs (Pending + InProgress) allowed across all manifests.
    /// </summary>
    /// <param name="maxJobs">
    /// The maximum active jobs (default: 10, null = unlimited). Must be at least 1 when set; the
    /// scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// When the total number of active jobs reaches this limit, the JobDispatcher dispatches no
    /// new work queue entries until existing jobs complete. The limit is approximate: each
    /// dispatching host counts active jobs on its own, so N hosts can reach N times the limit.
    /// </remarks>
    public SchedulerConfigurationBuilder MaxActiveJobs(int? maxJobs)
    {
        _configuration.MaxActiveJobs = maxJobs;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of queued work queue entries loaded per JobDispatcher cycle.
    /// </summary>
    /// <param name="limit">The maximum entries to load (default: 100, null = unlimited, minimum: 1)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Prevents the dispatcher from loading unbounded queued entries into memory. The default
    /// of 100 provides headroom beyond <see cref="MaxActiveJobs"/> for per-group limit skipping.
    /// Set to null to disable the limit.
    /// </remarks>
    public SchedulerConfigurationBuilder MaxQueuedJobsPerCycle(int? limit)
    {
        _configuration.MaxQueuedJobsPerCycle = limit.HasValue ? Math.Max(1, limit.Value) : null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of work queue entries created per ManifestManager polling cycle.
    /// </summary>
    /// <param name="limit">The maximum entries to create (default: 200, null = unlimited, minimum: 1)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Prevents a burst of DB writes after extended downtime when many manifests become due
    /// simultaneously. Excess manifests are deferred to the next cycle (default: 5 seconds).
    /// Set to null to disable the limit.
    /// </remarks>
    public SchedulerConfigurationBuilder MaxWorkQueueEntriesPerCycle(int? limit)
    {
        _configuration.MaxWorkQueueEntriesPerCycle = limit.HasValue
            ? Math.Max(1, limit.Value)
            : null;
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
    /// <param name="maxRetries">
    /// The maximum retry count (default: 3). Must not be negative; the scheduler refuses to build
    /// otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultMaxRetries(int maxRetries)
    {
        _configuration.DefaultMaxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Sets the default delay between retry attempts.
    /// </summary>
    /// <param name="delay">
    /// The retry delay (default: 5 minutes). Must be between zero and ten years; the scheduler
    /// refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultRetryDelay(TimeSpan delay)
    {
        _configuration.DefaultRetryDelay = delay;
        return this;
    }

    /// <summary>
    /// Sets the multiplier applied to retry delay on each subsequent retry.
    /// </summary>
    /// <param name="multiplier">
    /// The backoff multiplier (default: 2.0). Must be a finite number of at least 1; the scheduler
    /// refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder RetryBackoffMultiplier(double multiplier)
    {
        _configuration.RetryBackoffMultiplier = multiplier;
        return this;
    }

    /// <summary>
    /// Sets the maximum retry delay to prevent unbounded backoff growth.
    /// </summary>
    /// <param name="maxDelay">
    /// The maximum delay (default: 1 hour). Must be between zero and ten years; the scheduler
    /// refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder MaxRetryDelay(TimeSpan maxDelay)
    {
        _configuration.MaxRetryDelay = maxDelay;
        return this;
    }

    /// <summary>
    /// Sets how far back a manifest's failed runs are counted toward its retry backoff and its
    /// dead letter.
    /// </summary>
    /// <remarks>
    /// A failure older than the window no longer delays the next run or counts toward
    /// <c>MaxRetries</c>. A manifest scheduled with its own <c>FailureWindow</c> uses that
    /// instead. See <see cref="SchedulerConfiguration.FailureCountWindow"/>.
    ///
    /// The scheduler logs a warning when it starts if the retry backoff alone (the retry delay,
    /// its multiplier and <see cref="MaxRetryDelay"/>) spaces <see cref="DefaultMaxRetries"/>
    /// retries over the window or more: the count can then never be reached, and a manifest that
    /// always fails is retried for ever instead of being dead-lettered.
    /// </remarks>
    /// <param name="window">The failure count window (default: 24 hours)</param>
    /// <returns>The builder for method chaining</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="window"/> is not between one second and ten years.
    /// </exception>
    public SchedulerConfigurationBuilder FailureCountWindow(TimeSpan window)
    {
        if (SchedulerConfigLimits.PositiveDuration(window, nameof(window)) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(window), window, problem);

        _configuration.FailureCountWindow = window;
        return this;
    }

    /// <summary>
    /// Sets the timeout after which a running job is cancelled, for a run a scheduler dispatched
    /// whose manifest sets no Timeout of its own. A train nested inside a run shares that run's
    /// timeout; a run a scheduler did not dispatch is not bounded by it.
    /// </summary>
    /// <param name="timeout">
    /// The job timeout (default: 20 minutes). Must be between one second and ten years; the
    /// scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultJobTimeout(TimeSpan timeout)
    {
        _configuration.DefaultJobTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets how long a resolved (retried or acknowledged) dead letter is kept before the automatic
    /// purge deletes it. Dead letters awaiting intervention are never purged.
    /// </summary>
    /// <remarks>
    /// A retention saved from the dashboard or the <c>updateScheduler</c> mutation can lengthen
    /// this but not shorten it: when both state one, the longer applies, and the scheduler logs a
    /// warning. Without this call a saved retention replaces the default.
    /// </remarks>
    /// <param name="retention">
    /// The retention period (default: 30 days). Must be between zero and ten years; the scheduler
    /// refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DeadLetterRetentionPeriod(TimeSpan retention)
    {
        _configuration.DeadLetterRetentionPeriod = retention;
        _configuration.ConfiguredDeadLetterRetentionPeriod = retention;
        return this;
    }

    /// <summary>
    /// Sets whether resolved dead letters older than the retention period are deleted
    /// automatically. The purge reads this on every run, so it can also be turned off or on at
    /// runtime from the dashboard or the <c>updateScheduler</c> mutation.
    /// </summary>
    /// <remarks>
    /// The purge deletes, so it runs only when both this and a saved setting allow it: a saved
    /// <c>true</c> does not turn on a purge this call turned off, and the scheduler logs a warning
    /// when it ignores one. A saved <c>false</c> does turn it off. Without this call a saved
    /// setting replaces the default.
    /// </remarks>
    /// <param name="purge">True to purge resolved dead letters (default: true)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder AutoPurgeDeadLetters(bool purge = true)
    {
        _configuration.AutoPurgeDeadLetters = purge;
        _configuration.ConfiguredAutoPurgeDeadLetters = purge;
        return this;
    }

    /// <summary>
    /// Sets the timeout after which a Pending job that was never picked up is automatically failed.
    /// </summary>
    /// <param name="timeout">
    /// The stale pending timeout (default: 20 minutes). Must be between one second and ten years;
    /// the scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder StalePendingTimeout(TimeSpan timeout)
    {
        _configuration.StalePendingTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the timeout after which an InProgress job that never completed is automatically failed.
    /// A run whose own timeout is longer (its manifest's Timeout, or a longer
    /// <see cref="DefaultJobTimeout"/>) is kept until that timeout has passed as well.
    /// </summary>
    /// <param name="timeout">
    /// The stale in-progress timeout (default: 60 minutes). Must be between one second and ten
    /// years; the scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder StaleInProgressTimeout(TimeSpan timeout)
    {
        _configuration.StaleInProgressTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets how long a work queue entry may stay unconfirmed, in the middle of a two-phase
    /// enqueue, before it is resolved.
    /// </summary>
    /// <param name="timeout">
    /// The stale staged entry timeout (default: 10 minutes). Must be between one second and ten
    /// years; the scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder StaleStagedEntryTimeout(TimeSpan timeout)
    {
        _configuration.StaleStagedEntryTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Promotes a stale unconfirmed work queue entry instead of cancelling it.
    /// </summary>
    /// <remarks>
    /// Only for hosts whose deferring trains re-check in their chain whatever their
    /// <c>OnQueue</c> hook checked, and whose hooks are idempotent: a promoted entry may be a
    /// mutation whose hook never ran, or one the hook rejected.
    /// </remarks>
    /// <param name="promote">
    /// Whether to promote rather than cancel (default: <c>true</c>). Takes a parameter so the
    /// choice can come from configuration, which is what its neighbours
    /// <see cref="RecoverStuckJobsOnStartup"/> and <see cref="PruneOrphanedManifests"/> already do;
    /// without one, a host reading this from settings had to branch around the call.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder PromoteStaleStagedEntries(bool promote = true)
    {
        _configuration.PromoteStaleStagedEntries = promote;
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
    /// <param name="threshold">
    /// The misfire threshold (default: 60 seconds). Must be between zero and ten years; the
    /// scheduler refuses to build otherwise.
    /// </param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultMisfireThreshold(TimeSpan threshold)
    {
        _configuration.DefaultMisfireThreshold = threshold;
        return this;
    }

    /// <summary>
    /// Sets whether to automatically recover stuck jobs on scheduler startup.
    /// </summary>
    /// <remarks>
    /// When enabled, every <c>InProgress</c> run in the shared database whose <c>StartTime</c> is
    /// earlier than this host's start is marked <c>Failed</c> ("Server restarted while job was in
    /// progress").
    /// That is every such run, whichever host or worker is executing it: the recovery does not
    /// know which runs belonged to this process, so on a deployment where several hosts or
    /// remote workers share the database, starting one host also fails runs that are still
    /// executing elsewhere. The recovery itself requeues nothing. Skipped when no database
    /// provider is registered.
    /// </remarks>
    /// <param name="recover">True to recover stuck jobs (default: true)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder RecoverStuckJobsOnStartup(bool recover = true)
    {
        _configuration.RecoverStuckJobsOnStartup = recover;
        return this;
    }

    /// <summary>
    /// Uses the in-memory job submitter for testing and development.
    /// </summary>
    /// <remarks>
    /// Overrides the default <see cref="PostgresJobSubmitter"/>.
    /// The in-memory submitter executes jobs immediately and synchronously.
    /// Useful for unit/integration testing without external infrastructure.
    /// </remarks>
    /// <returns>The builder for method chaining</returns>
    internal SchedulerConfigurationBuilder UseInMemoryWorkers()
    {
        _taskServerRegistration = services =>
        {
            services.AddScoped<IJobSubmitter, InMemoryJobSubmitter>();
        };
        return this;
    }

    /// <summary>
    /// Configures local worker thread options (worker count, polling interval, timeouts).
    /// </summary>
    /// <remarks>
    /// Local workers are enabled by default when PostgreSQL is configured. Use this method
    /// to customize worker behavior. If not called, defaults are used
    /// (<see cref="LocalWorkerOptions"/> for default values).
    /// </remarks>
    /// <param name="configure">Action to configure local worker options</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder ConfigureLocalWorkers(Action<LocalWorkerOptions> configure)
    {
        configure(_localWorkerOptions);
        return this;
    }

    /// <summary>
    /// Routes specific trains to a remote HTTP endpoint for execution.
    /// </summary>
    /// <remarks>
    /// Trains not included in the <paramref name="routing"/> configuration continue to execute
    /// locally via <see cref="PostgresJobSubmitter"/> and <c>LocalWorkerService</c>.
    /// Only the trains specified via <c>ForTrain&lt;T&gt;()</c> are dispatched to the remote endpoint.
    ///
    /// Trains can also be marked with <c>[TraxRemote]</c> to opt into remote execution without
    /// explicit <c>ForTrain&lt;T&gt;()</c> routing. Builder routing takes precedence over the attribute.
    /// A scheduler with a <c>[TraxRemote]</c> train and no remote submitter at all (this,
    /// <c>UseRemoteWorkers</c>, <c>UseSqsWorkers</c> or <c>UseLambdaWorkers</c>) refuses to build,
    /// rather than run the train locally.
    ///
    /// Jobs are POSTed as JSON to the configured <see cref="RemoteWorkerOptions.BaseUrl"/>.
    /// The remote endpoint runs <see cref="Trains.JobRunner.JobRunnerTrain"/> to execute the train.
    ///
    /// The runner refuses requests that do not meet its posture. Set
    /// <see cref="RemoteWorkerOptions.SigningKey"/> to the key the runner verifies, or use
    /// <see cref="RemoteWorkerOptions.ConfigureHttpClient"/> to add the credentials its
    /// authorization policy expects.
    ///
    /// Set up the remote side with <c>AddTraxJobRunner(runner => ...)</c> and <c>UseTraxJobRunner()</c>.
    ///
    /// Call it once per endpoint to route different trains to different runners. Each call keeps
    /// its own options and its own HTTP client, so a train is sent only to the endpoint it is
    /// routed to, signed with that endpoint's key and carrying only the headers its
    /// <see cref="RemoteWorkerOptions.ConfigureHttpClient"/> added. A train routed by two calls
    /// is refused when the scheduler is built. A <c>[TraxRemote]</c> train that no call routes
    /// explicitly goes to the first routed registration (this one, <c>UseSqsWorkers</c> or
    /// <c>UseLambdaWorkers</c>, whichever was added first).
    /// </remarks>
    /// <param name="configure">Action to configure the remote endpoint URL and HTTP client</param>
    /// <param name="routing">Action to specify which trains should be dispatched remotely</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseRemoteWorkers(
        Action<RemoteWorkerOptions> configure,
        Action<SubmitterRouting>? routing = null
    )
    {
        var options = new RemoteWorkerOptions();
        configure(options);
        if (options.SigningKey is not null)
            Services.RequestSigning.RunnerRequestSignature.EnsureKey(
                options.SigningKey,
                nameof(RemoteWorkerOptions.SigningKey)
            );

        var submitterRouting = new SubmitterRouting();
        routing?.Invoke(submitterRouting);

        // Each call has its own named client, so its base address, timeout and headers, and its
        // own options, including its signing key, stay with the endpoint they were given for.
        var clientName = $"Trax.RemoteWorkers.{_routedSubmitterRegistrations.Count}";

        _routedSubmitterRegistrations.Add(
            new RoutedSubmitterRegistration(
                submitterRouting,
                typeof(Services.JobSubmitter.HttpJobSubmitter),
                services =>
                {
                    services.AddHttpClient(
                        clientName,
                        client =>
                        {
                            client.BaseAddress = new Uri(options.BaseUrl);
                            client.Timeout = options.Timeout;
                            options.ConfigureHttpClient?.Invoke(client);
                        }
                    );
                }
            )
            {
                CreateSubmitter = services => new Services.JobSubmitter.HttpJobSubmitter(
                    services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName),
                    options,
                    services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.JobSubmitter.HttpJobSubmitter>>()
                ),
                Description = $"UseRemoteWorkers({options.BaseUrl})",
            }
        );
        return this;
    }

    /// <summary>
    /// Offloads synchronous run execution to a remote HTTP endpoint.
    /// </summary>
    /// <remarks>
    /// Overrides the default <see cref="LocalRunExecutor"/> with <see cref="HttpRunExecutor"/>.
    /// When a GraphQL <c>run*</c> mutation is called, the request is POSTed to the configured
    /// <see cref="RemoteRunOptions.BaseUrl"/> and blocks until the train completes.
    /// The remote endpoint returns the serialized train output in the response body.
    ///
    /// Without this, runs execute in-process via <see cref="LocalRunExecutor"/> (the default).
    ///
    /// Set up the remote side with <c>AddTraxJobRunner(runner => ...)</c> and
    /// <c>UseTraxRunEndpoint()</c> in the runner process, and set
    /// <see cref="RemoteRunOptions.SigningKey"/> to the key it verifies.
    /// </remarks>
    /// <param name="configure">Action to configure the remote endpoint URL and HTTP client</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseRemoteRun(Action<RemoteRunOptions> configure)
    {
        _remoteRunRegistration = services =>
        {
            var options = new RemoteRunOptions();
            configure(options);
            if (options.SigningKey is not null)
                Services.RequestSigning.RunnerRequestSignature.EnsureKey(
                    options.SigningKey,
                    nameof(RemoteRunOptions.SigningKey)
                );
            services.AddSingleton(options);

            services.AddHttpClient<IRunExecutor, HttpRunExecutor>(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = options.Timeout;
                options.ConfigureHttpClient?.Invoke(client);
            });
        };
        return this;
    }

    /// <summary>
    /// Overrides the default job submitter with a custom implementation.
    /// </summary>
    /// <remarks>
    /// Use this as an escape hatch when the built-in submitters don't fit your use case.
    /// Most users should use <see cref="UseRemoteWorkers"/> instead.
    /// When no override is configured, the scheduler defaults to <see cref="PostgresJobSubmitter"/>
    /// (with <c>UsePostgres()</c>) or <see cref="InMemoryJobSubmitter"/> (without a database provider).
    /// </remarks>
    /// <param name="registration">The action to register your custom <see cref="IJobSubmitter"/></param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder OverrideSubmitter(Action<IServiceCollection> registration)
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
