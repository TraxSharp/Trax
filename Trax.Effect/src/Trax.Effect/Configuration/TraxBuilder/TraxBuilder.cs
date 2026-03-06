using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Effect.Configuration.TraxBuilder;

/// <summary>
/// Root builder for configuring the Trax system.
/// </summary>
/// <remarks>
/// Each subsystem (effects, mediator, scheduler) has its own scoped builder,
/// accessible via extension methods on this type:
/// <code>
/// services.AddTrax(trax => trax
///     .AddEffects(effects => effects
///         .UsePostgres(connectionString)
///         .AddJson()
///         .SaveTrainParameters()
///     )
///     .AddMediator(typeof(Program).Assembly)
///     .AddScheduler(scheduler => scheduler
///         .UseLocalWorkers()
///         .Schedule&lt;IMyTrain&gt;(...)
///     )
/// );
/// </code>
/// </remarks>
public class TraxBuilder(IServiceCollection services, IEffectRegistry registry)
{
    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    public IServiceCollection ServiceCollection => services;

    /// <summary>
    /// Gets the effect registry for registering effect providers.
    /// </summary>
    internal IEffectRegistry EffectRegistry => registry;

    /// <summary>
    /// Gets or sets the effect configuration, populated by <c>AddEffects()</c>.
    /// </summary>
    internal TraxEffectConfiguration.TraxEffectConfiguration? EffectConfiguration { get; set; }
}
