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
    public TraxBuilder Root { get; }

    public TraxBuilderWithEffects(TraxBuilder root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    public IServiceCollection ServiceCollection => Root.ServiceCollection;
}
