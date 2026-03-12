using Trax.Mediator.Configuration;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for configuring Trax.Scheduler services.
/// </summary>
public static class SchedulerExtensions
{
    /// <summary>
    /// Adds the Trax scheduler to the configuration.
    /// </summary>
    /// <param name="builder">The builder after mediator has been configured</param>
    /// <param name="configure">
    /// A function that configures the scheduler builder. Chain calls fluently and return the builder.
    /// </param>
    /// <returns>A <see cref="TraxBuilderWithMediator"/> for continued chaining</returns>
    /// <remarks>
    /// <see cref="Services.JobSubmitter.PostgresJobSubmitter"/> is registered automatically
    /// as the default <see cref="Services.JobSubmitter.IJobSubmitter"/>. Call
    /// <c>UseLocalWorkers()</c> to also start worker threads that execute jobs in-process.
    /// Omit <c>UseLocalWorkers()</c> to run as a scheduler-only process that writes jobs
    /// for a separate worker process to consume.
    ///
    /// <code>
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects
    ///         .UsePostgres(connectionString)
    ///     )
    ///     .AddMediator(typeof(Program).Assembly)
    ///     .AddScheduler(scheduler => scheduler
    ///         .Schedule&lt;IMyTrain&gt;("my-job", new MyInput(), Every.Minutes(5))
    ///     )
    /// );
    /// </code>
    /// </remarks>
    public static TraxBuilderWithMediator AddScheduler(
        this TraxBuilderWithMediator builder,
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
    )
    {
        var schedulerBuilder = new SchedulerConfigurationBuilder(builder);
        configure(schedulerBuilder);
        schedulerBuilder.Build();
        return builder;
    }

    /// <summary>
    /// Adds the Trax scheduler with default settings.
    /// </summary>
    /// <param name="builder">The builder after mediator has been configured</param>
    /// <returns>A <see cref="TraxBuilderWithMediator"/> for continued chaining</returns>
    /// <remarks>
    /// Registers the scheduler with all default settings. Equivalent to
    /// <c>AddScheduler(_ => { })</c>.
    ///
    /// <see cref="Services.JobSubmitter.PostgresJobSubmitter"/> is registered as the default
    /// <see cref="Services.JobSubmitter.IJobSubmitter"/>. A data provider
    /// (<c>UsePostgres()</c>) must be configured in <c>AddEffects()</c> for the scheduler
    /// to function at runtime.
    /// </remarks>
    public static TraxBuilderWithMediator AddScheduler(this TraxBuilderWithMediator builder)
    {
        return builder.AddScheduler(scheduler => scheduler);
    }
}
