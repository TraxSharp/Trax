using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Services.RunExecutor;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;

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
    /// <param name="interval">The polling interval (default: 2 seconds)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder JobDispatcherPollingInterval(TimeSpan interval)
    {
        _configuration.JobDispatcherPollingInterval = interval;
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
    /// <param name="timeout">The job timeout (default: 20 minutes)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder DefaultJobTimeout(TimeSpan timeout)
    {
        _configuration.DefaultJobTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the timeout after which a Pending job that was never picked up is automatically failed.
    /// </summary>
    /// <param name="timeout">The stale pending timeout (default: 20 minutes)</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder StalePendingTimeout(TimeSpan timeout)
    {
        _configuration.StalePendingTimeout = timeout;
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
    ///
    /// Jobs are POSTed as JSON to the configured <see cref="RemoteWorkerOptions.BaseUrl"/>.
    /// The remote endpoint runs <see cref="Trains.JobRunner.JobRunnerTrain"/> to execute the train.
    ///
    /// Trax does not bake in any authentication. Use <see cref="RemoteWorkerOptions.ConfigureHttpClient"/>
    /// to add authorization headers or any custom HTTP configuration.
    ///
    /// Set up the remote side with <c>AddTraxJobRunner()</c> and <c>UseTraxJobRunner()</c>.
    /// </remarks>
    /// <param name="configure">Action to configure the remote endpoint URL and HTTP client</param>
    /// <param name="routing">Action to specify which trains should be dispatched remotely</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseRemoteWorkers(
        Action<RemoteWorkerOptions> configure,
        Action<SubmitterRouting> routing
    )
    {
        var options = new RemoteWorkerOptions();
        configure(options);

        var submitterRouting = new SubmitterRouting();
        routing(submitterRouting);

        _routedSubmitterRegistrations.Add(
            new RoutedSubmitterRegistration(
                submitterRouting,
                typeof(Services.JobSubmitter.HttpJobSubmitter),
                services =>
                {
                    services.AddSingleton(options);

                    services.AddHttpClient<Services.JobSubmitter.HttpJobSubmitter>(client =>
                    {
                        client.BaseAddress = new Uri(options.BaseUrl);
                        client.Timeout = options.Timeout;
                        options.ConfigureHttpClient?.Invoke(client);
                    });
                }
            )
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
    /// Set up the remote side with <c>UseTraxRunEndpoint()</c> in the runner process.
    /// </remarks>
    /// <param name="configure">Action to configure the remote endpoint URL and HTTP client</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder UseRemoteRun(Action<RemoteRunOptions> configure)
    {
        _remoteRunRegistration = services =>
        {
            var options = new RemoteRunOptions();
            configure(options);
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
