using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Singleton registry that maps a JWT scheme name to the factory that
/// produces its <see cref="ITraxPrincipalResolver{JwtTokenInput}"/>.
/// Populated by <c>AddTraxJwtAuth</c> at startup and read by the
/// <c>OnTokenValidated</c> callback at request time.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
internal sealed class JwtResolverRegistry
{
    private readonly Dictionary<
        string,
        Func<IServiceProvider, ITraxPrincipalResolver<JwtTokenInput>>
    > _byScheme = new(StringComparer.Ordinal);

    /// <summary>
    /// All scheme names registered with this registry. Returned in insertion
    /// order.
    /// </summary>
    public IReadOnlyCollection<string> SchemeNames => _byScheme.Keys;

    /// <summary>
    /// Registers (or replaces) the factory for a scheme. Replacing is the
    /// documented behavior so consumers can shadow Trax's default resolver
    /// with their own without rewiring the scheme.
    /// </summary>
    public void Register(
        string schemeName,
        Func<IServiceProvider, ITraxPrincipalResolver<JwtTokenInput>> factory
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(factory);
        _byScheme[schemeName] = factory;
    }

    /// <summary>
    /// Resolves the resolver for the given scheme. Throws when the scheme
    /// has no registered factory, which indicates a misconfiguration in
    /// <c>AddTraxJwtAuth</c> rather than an authentication failure.
    /// </summary>
    public ITraxPrincipalResolver<JwtTokenInput> Resolve(string schemeName, IServiceProvider sp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(sp);
        if (!_byScheme.TryGetValue(schemeName, out var factory))
            throw new InvalidOperationException(
                $"No ITraxPrincipalResolver<JwtTokenInput> registered for scheme '{schemeName}'. "
                    + "Call AddTraxJwtAuth(schemeName, ...) before UseAuthentication."
            );
        return factory(sp);
    }

    /// <summary>
    /// Returns the resolver for <paramref name="schemeName"/>, or <c>null</c>
    /// if none is registered. Used by adapters that want to no-op when a
    /// scheme is not Trax-managed (e.g. third-party JWT schemes registered
    /// alongside Trax ones).
    /// </summary>
    public ITraxPrincipalResolver<JwtTokenInput>? TryResolve(string schemeName, IServiceProvider sp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(sp);
        return _byScheme.TryGetValue(schemeName, out var factory) ? factory(sp) : null;
    }

    /// <summary>
    /// Returns the registered resolver for the default Trax JWT scheme, or
    /// <c>null</c> if the default scheme has not been registered. Used by
    /// the GraphQL subscription interceptor for back-compat with the
    /// single-scheme setup.
    /// </summary>
    public ITraxPrincipalResolver<JwtTokenInput>? ResolveDefault(IServiceProvider sp) =>
        TryResolve(JwtDefaults.SchemeName, sp);

    internal static JwtResolverRegistry GetOrAdd(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(sd => sd.ServiceType == typeof(JwtResolverRegistry));
        if (existing?.ImplementationInstance is JwtResolverRegistry registry)
            return registry;

        registry = new JwtResolverRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
