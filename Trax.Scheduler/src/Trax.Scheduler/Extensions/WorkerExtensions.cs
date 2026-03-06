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
    /// <remarks>
    /// This registers the execution pipeline (via <see cref="JobRunnerExtensions.AddTraxJobRunner"/>)
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
        // Register the execution pipeline
        services.AddTraxJobRunner();

        // Configure worker options
        var options = new LocalWorkerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Register the worker service that polls background_job
        services.AddHostedService<Services.LocalWorkerService.LocalWorkerService>();

        return services;
    }
}
