using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for configuring Trax.Scheduler services.
/// </summary>
public static class SchedulerExtensions
{
    /// <summary>
    /// Adds the Trax.Core scheduler as part of the effects configuration.
    /// </summary>
    /// <param name="builder">The Trax.Core effect configuration builder</param>
    /// <param name="configure">Action to configure the scheduler</param>
    /// <returns>The effect configuration builder for continued chaining</returns>
    /// <remarks>
    /// Configure the scheduler within the Trax.Core effects setup:
    ///
    /// <code>
    /// services.AddTraxEffects(options => options
    ///     .AddEffectTrainBus(assemblies)
    ///     .AddPostgresEffect(connectionString)
    ///     .AddScheduler(scheduler => scheduler
    ///         .PollingInterval(TimeSpan.FromSeconds(30))
    ///         .MaxActiveJobs(100)
    ///         .DefaultMaxRetries(5)
    ///         .UseHangfire(
    ///             config => config.UsePostgreSqlStorage(...),
    ///             server => server.WorkerCount = 4
    ///         )
    ///     )
    /// );
    /// </code>
    /// </remarks>
    public static TraxEffectConfigurationBuilder AddScheduler(
        this TraxEffectConfigurationBuilder builder,
        Action<SchedulerConfigurationBuilder> configure
    )
    {
        var schedulerBuilder = new SchedulerConfigurationBuilder(builder);
        configure(schedulerBuilder);
        return schedulerBuilder.Build();
    }
}
