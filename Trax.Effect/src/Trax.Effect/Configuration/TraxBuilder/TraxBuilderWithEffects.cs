using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Configuration.TraxBuilder;

/// <summary>
/// Builder state after <c>AddEffects()</c> has been called.
/// Extension methods for <c>AddMediator()</c> target this type,
/// enforcing that effects are configured before the mediator.
/// </summary>
public class TraxBuilderWithEffects
{
    /// <summary>
    /// The root builder that holds accumulated configuration state.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TraxBuilder Root { get; }

    public TraxBuilderWithEffects(TraxBuilder root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection ServiceCollection => Root.ServiceCollection;

    /// <summary>
    /// Whether a database-backed data provider (e.g., Postgres) was configured.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDatabaseProvider => Root.HasDatabaseProvider;

    /// <summary>
    /// Whether any data provider (<c>UsePostgres()</c> or <c>UseInMemory()</c>) was configured.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDataProvider => Root.HasDataProvider;
}
