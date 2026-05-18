using Microsoft.Extensions.DependencyInjection;

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
    {
        Services = services;
        ConfigBuilder = configBuilder;
    }

    /// <summary>The DI container that <c>AddTraxGraphQLClient</c> was called against.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// The underlying configuration builder. Exposed for integration packages that need to
    /// read/write configuration state during their own <c>Use*</c> methods. Most consumers
    /// should not touch this directly - use the fluent methods on this type instead.
    /// </summary>
    internal GraphQLClientConfigurationBuilder ConfigBuilder { get; }
}
