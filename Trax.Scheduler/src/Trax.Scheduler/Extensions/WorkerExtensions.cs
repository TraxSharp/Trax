using Microsoft.Extensions.DependencyInjection;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for setting up a standalone worker process that polls the
/// <c>background_job</c> table and executes trains without the full scheduler.
/// </summary>
public static class WorkerExtensions
{
    /// <summary>
    /// Registers a standalone worker that polls the <c>background_job</c> table for jobs.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional callback to customize worker count, polling interval, and timeouts</param>
    /// <returns>The service collection for continued chaining</returns>
    /// <exception cref="InvalidOperationException">
    /// An option is outside its range (see <see cref="LocalWorkerOptions"/>), or the host already
    /// runs a local worker pool.
    /// </exception>
    /// <remarks>
    /// This registers the execution pipeline (via <see cref="JobRunnerExtensions.AddTraxJobRunner(IServiceCollection)"/>)
    /// plus <see cref="Services.LocalWorkerService.LocalWorkerService"/> as a hosted service.
    /// No ManifestManager, no JobDispatcher — just the worker loop.
    ///
    /// The standalone worker must also call <c>AddTrax()</c> with <c>AddEffects()</c>,
    /// <c>UsePostgres()</c>, and <c>AddMediator()</c> to register the effect system and train assemblies.
    /// </remarks>
    public static IServiceCollection AddTraxWorker(
        this IServiceCollection services,
        Action<LocalWorkerOptions>? configure = null
    )
    {
        // A scheduler host already runs a worker pool with its own options; a second set would
        // silently replace them (or be ignored), so the two are refused together.
        if (services.Any(d => d.ServiceType == typeof(LocalWorkerOptions)))
            throw new InvalidOperationException(LocalWorkerOptions.RegisteredTwiceMessage);

        // Register the execution pipeline
        services.AddTraxJobRunner();

        // Configure worker options
        var options = new LocalWorkerOptions();
        configure?.Invoke(options);
        var problems = options.Problems(nameof(AddTraxWorker)).ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "The worker options have values the worker pool cannot run with. "
                    + string.Join(" ", problems)
            );
        services.AddSingleton(options);

        // Register the worker service that polls background_job
        services.AddHostedService<Services.LocalWorkerService.LocalWorkerService>();

        return services;
    }
}
