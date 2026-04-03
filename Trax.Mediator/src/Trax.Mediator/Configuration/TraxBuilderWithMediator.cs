using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;

namespace Trax.Mediator.Configuration;

/// <summary>
/// Builder state after <c>AddMediator()</c> has been called.
/// Extension methods for <c>AddScheduler()</c> target this type,
/// enforcing that the mediator is configured before the scheduler.
/// </summary>
public class TraxBuilderWithMediator
{
    internal TraxBuilderWithEffects EffectsBuilder { get; }

    public TraxBuilderWithMediator(TraxBuilderWithEffects effectsBuilder)
    {
        EffectsBuilder = effectsBuilder;
    }

    internal TraxBuilder Root => EffectsBuilder.Root;

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection ServiceCollection => Root.ServiceCollection;

    /// <summary>
    /// Whether a database-backed data provider (e.g., Postgres) was configured in <c>AddEffects()</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDatabaseProvider => Root.HasDatabaseProvider;

    /// <summary>
    /// Whether any data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>) was configured.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDataProvider => Root.HasDataProvider;
}
