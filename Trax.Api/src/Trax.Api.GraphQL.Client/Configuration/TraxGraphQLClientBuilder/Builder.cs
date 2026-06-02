using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Fluent builder returned by <see cref="ServiceExtensions.AddTraxGraphQLClient"/>. Chain
/// <c>.UseFileSchema(...)</c>, <c>.UseAssemblySchema(...)</c>, <c>.WithStrictness(...)</c>,
/// <c>.UseStartupValidation(...)</c>, etc. The builder mutates the underlying configuration
/// and DI registrations in-place, so the chain can stop at any point and the registered
/// kernel is consistent with the calls so far.
///
/// Split across partial files by feature area per the Trax convention:
/// <list type="bullet">
///   <item><c>Builder.cs</c> - state and constructor.</item>
///   <item><c>Builder.Schema.cs</c> - <c>UseIntrospection</c>, <c>UseFileSchema</c>.</item>
///   <item><c>Builder.Options.cs</c> - <c>WithStrictness</c>, <c>ConfigureOptions</c>.</item>
/// </list>
/// Integration packages contribute additional methods via extension methods on this type
/// (e.g. <c>UseAssemblySchema</c>, <c>UseStartupValidation</c> in <c>Trax.Api.GraphQL.Client.Trax</c>).
/// </summary>
public sealed partial class TraxGraphQLClientBuilder
{
    internal TraxGraphQLClientBuilder(
        IServiceCollection services,
        GraphQLClientConfigurationBuilder configBuilder
    )
        : this(services, configBuilder, serviceKey: null) { }

    internal TraxGraphQLClientBuilder(
        IServiceCollection services,
        GraphQLClientConfigurationBuilder configBuilder,
        object? serviceKey
    )
    {
        Services = services;
        ConfigBuilder = configBuilder;
        ServiceKey = serviceKey;
    }

    /// <summary>The DI container that <c>AddTraxGraphQLClient</c> was called against.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// The underlying configuration builder. Exposed for integration packages that need to
    /// read/write configuration state during their own <c>Use*</c> methods. Most consumers
    /// should not touch this directly - use the fluent methods on this type instead.
    /// </summary>
    internal GraphQLClientConfigurationBuilder ConfigBuilder { get; }

    /// <summary>
    /// The DI key this client was registered under, or <c>null</c> for the unkeyed
    /// (<c>AddTraxGraphQLClient</c>) registration. Builder methods that re-register or resolve
    /// services use this so keyed and unkeyed clients behave identically.
    /// </summary>
    internal object? ServiceKey { get; }

    /// <summary>
    /// Resolves the <see cref="IGraphQLClientConfiguration"/> for this builder's registration,
    /// keyed or unkeyed.
    /// </summary>
    internal IGraphQLClientConfiguration ResolveConfiguration(IServiceProvider sp) =>
        ServiceKey is null
            ? sp.GetRequiredService<IGraphQLClientConfiguration>()
            : sp.GetRequiredKeyedService<IGraphQLClientConfiguration>(ServiceKey);

    /// <summary>
    /// Resolves the <see cref="IGraphQLClientValidator"/> for this builder's registration,
    /// keyed or unkeyed.
    /// </summary>
    internal IGraphQLClientValidator ResolveValidator(IServiceProvider sp) =>
        ServiceKey is null
            ? sp.GetRequiredService<IGraphQLClientValidator>()
            : sp.GetRequiredKeyedService<IGraphQLClientValidator>(ServiceKey);

    /// <summary>
    /// Replaces the <see cref="ISchemaProvider"/> registration for this builder. For the unkeyed
    /// builder this is a straight <see cref="ServiceCollectionDescriptorExtensions.Replace"/>;
    /// for a keyed builder it removes the existing keyed descriptor for this key (Microsoft DI
    /// has no keyed <c>Replace</c>) and adds a new keyed singleton.
    /// </summary>
    internal void ReplaceSchemaProvider(Func<IServiceProvider, ISchemaProvider> factory)
    {
        if (ServiceKey is null)
        {
            Services.Replace(ServiceDescriptor.Singleton(factory));
            return;
        }

        for (var i = Services.Count - 1; i >= 0; i--)
        {
            var descriptor = Services[i];
            if (
                descriptor.ServiceType == typeof(ISchemaProvider)
                && descriptor.IsKeyedService
                && Equals(descriptor.ServiceKey, ServiceKey)
            )
            {
                Services.RemoveAt(i);
            }
        }

        Services.AddKeyedSingleton<ISchemaProvider>(ServiceKey, (sp, _) => factory(sp));
    }
}
