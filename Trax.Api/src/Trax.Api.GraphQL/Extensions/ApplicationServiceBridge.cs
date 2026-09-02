using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Extensions;

/// <summary>
/// Bridges application-container services into HotChocolate's schema container.
/// </summary>
/// <remarks>
/// HotChocolate 16 stopped forwarding unresolved schema-container lookups to the
/// application container, so anything HotChocolate activates itself — request
/// interceptors, socket-session interceptors, diagnostic listeners — can no longer see
/// the host's services. <c>AddApplicationService&lt;T&gt;</c> re-exposes one of them.
/// <para>
/// It resolves the bridged service eagerly while the schema container is being built, so
/// bridging something the host never registered turns an optional dependency into a hard
/// startup failure. Trax's interceptors are wired conditionally (api-key auth, JWT auth,
/// audit), so the bridge has to be conditional too.
/// </para>
/// </remarks>
internal static class ApplicationServiceBridge
{
    /// <summary>
    /// Bridges <typeparamref name="TService"/> when <paramref name="services"/> actually
    /// contains a registration for it, and does nothing otherwise.
    /// </summary>
    public static IRequestExecutorBuilder BridgeApplicationService<TService>(
        this IRequestExecutorBuilder builder,
        IServiceCollection services
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        if (IsRegistered<TService>(services))
            builder.AddApplicationService<TService>();

        return builder;
    }

    /// <summary>
    /// True when the collection can satisfy <typeparamref name="TService"/>, either from a
    /// closed registration or from an open-generic one (<c>ILogger&lt;&gt;</c>,
    /// <c>IOptions&lt;&gt;</c>, and friends are registered open).
    /// </summary>
    internal static bool IsRegistered<TService>(IServiceCollection services)
    {
        var serviceType = typeof(TService);
        var openDefinition = serviceType.IsGenericType
            ? serviceType.GetGenericTypeDefinition()
            : null;

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == serviceType)
                return true;

            if (openDefinition is not null && descriptor.ServiceType == openDefinition)
                return true;
        }

        return false;
    }
}
