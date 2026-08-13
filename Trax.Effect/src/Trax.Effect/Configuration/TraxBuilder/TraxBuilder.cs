using System.ComponentModel;
using System.Reflection;
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
///         .Schedule&lt;IMyTrain&gt;(...)
///     )
/// );
/// </code>
/// </remarks>
public partial class TraxBuilder(IServiceCollection services, IEffectRegistry registry)
{
    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection ServiceCollection => services;

    /// <summary>
    /// Gets the effect registry for registering effect providers.
    /// </summary>
    internal IEffectRegistry EffectRegistry => registry;

    /// <summary>
    /// Gets or sets the effect configuration, populated by <c>AddEffects()</c>.
    /// </summary>
    internal TraxEffectConfiguration.TraxEffectConfiguration? EffectConfiguration { get; set; }

    /// <summary>
    /// Whether a database-backed data provider (e.g., Postgres) was configured.
    /// When false, downstream builders (e.g., the scheduler) default to in-memory implementations.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDatabaseProvider { get; set; }

    /// <summary>
    /// Whether any data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>) was configured.
    /// Unlike <see cref="HasDatabaseProvider"/> (Postgres only), this is true for all data providers.
    /// Used for build-time validation of features that require any data context.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDataProvider { get; set; }

    /// <summary>
    /// Assemblies a subsystem contributes for the mediator to scan so its routes become dispatchable, without
    /// the host naming them. For example <c>AddStateMachines(...)</c> adds its generic state-machine mutations'
    /// assembly here; <c>AddMediator(...)</c> merges these into its scan at build time. Populated before
    /// <c>AddMediator</c> runs (the fluent chain enforces the order).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<Assembly> ContributedMediatorAssemblies { get; } = [];

    /// <summary>
    /// Set once <c>AddMediator</c> has built the train registry. A subsystem that contributes mediator
    /// assemblies checks this to fail fast if it is called after <c>AddMediator</c>, since its routes would
    /// arrive too late to be dispatchable.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool MediatorConfigured { get; set; }
}
