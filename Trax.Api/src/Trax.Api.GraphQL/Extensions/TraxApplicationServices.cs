namespace Trax.Api.GraphQL.Extensions;

/// <summary>
/// Holds the application container so schema components can reach it.
/// </summary>
/// <remarks>
/// HotChocolate 16 builds its schema components from a separate container that does not
/// forward to the application container.
/// <see cref="ApplicationServiceBridge.BridgeApplicationService{TService}"/> re-exposes a
/// specific service across that boundary, but a component that resolves services
/// dynamically — by scheme name, by type discovered at runtime — needs the provider
/// itself, and <c>IServiceProvider</c>/<c>IServiceScopeFactory</c> cannot be bridged: the
/// schema container registers its own and those win.
/// <para>
/// Wrapping it in a Trax-owned type sidesteps that, because nothing else registers this
/// type.
/// </para>
/// </remarks>
public sealed class TraxApplicationServices(IServiceProvider services)
{
    /// <summary>The application container, with the host's scoped registrations.</summary>
    public IServiceProvider Services { get; } =
        services ?? throw new ArgumentNullException(nameof(services));
}
