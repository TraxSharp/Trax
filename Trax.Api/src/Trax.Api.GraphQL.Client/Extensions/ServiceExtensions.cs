using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers the Trax GraphQL client kernel against the supplied endpoint. Returns a
    /// <see cref="TraxGraphQLClientBuilder"/> for fluent configuration:
    /// <code>
    /// services.AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    ///         .UseFileSchema("schema.graphql")
    ///         .WithStrictness(ResponseStrictness.ThrowOnDrift);
    /// </code>
    /// Calling without chaining is valid: it registers the kernel with default settings
    /// (<see cref="IntrospectingSchemaProvider"/>, <see cref="ResponseStrictness.Lenient"/>).
    /// </summary>
    public static TraxGraphQLClientBuilder AddTraxGraphQLClient(
        this IServiceCollection services,
        Uri baseAddress
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var configBuilder = new GraphQLClientConfigurationBuilder(baseAddress);
        Register(services, configBuilder, serviceKey: null);
        return new TraxGraphQLClientBuilder(services, configBuilder);
    }

    /// <summary>
    /// Registers a Trax GraphQL client kernel under the supplied <paramref name="serviceKey"/>,
    /// so multiple clients pointing at different servers can coexist in one
    /// <see cref="IServiceCollection"/>. Resolve the executor with
    /// <c>[FromKeyedServices(serviceKey)] IGraphQLClientExecutor</c> or
    /// <c>GetRequiredKeyedService&lt;IGraphQLClientExecutor&gt;(serviceKey)</c>:
    /// <code>
    /// services.AddKeyedTraxGraphQLClient("serverB", new Uri("https://b.example.com/graphql"))
    ///         .UseFileSchema("b.graphql");
    /// services.AddKeyedTraxGraphQLClient("serverC", new Uri("https://c.example.com/graphql"))
    ///         .UseFileSchema("c.graphql");
    /// </code>
    /// The key identifies the downstream server; each key gets its own configuration,
    /// <see cref="HttpClient"/>, schema provider, and validator cache.
    /// </summary>
    public static TraxGraphQLClientBuilder AddKeyedTraxGraphQLClient(
        this IServiceCollection services,
        object serviceKey,
        Uri baseAddress
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var configBuilder = new GraphQLClientConfigurationBuilder(baseAddress);
        Register(services, configBuilder, serviceKey);
        return new TraxGraphQLClientBuilder(services, configBuilder, serviceKey);
    }

    /// <summary>
    /// Registers the four client services either unkeyed (<paramref name="serviceKey"/> is
    /// <c>null</c>) or keyed. Keyed registrations resolve their dependencies by the same key,
    /// because Microsoft DI does not cascade the key to a service's own constructor arguments.
    /// The configuration is built lazily so chained builder calls can mutate state before DI
    /// resolves the singleton.
    /// </summary>
    private static void Register(
        IServiceCollection services,
        GraphQLClientConfigurationBuilder configBuilder,
        object? serviceKey
    )
    {
        if (serviceKey is null)
        {
            services.AddSingleton<IGraphQLClientConfiguration>(_ => configBuilder.Build());
            services.AddSingleton<ISchemaProvider, IntrospectingSchemaProvider>();
            services.AddSingleton<IGraphQLClientValidator, GraphQLClientValidator>();
            services.AddSingleton<IGraphQLClientExecutor, GraphQLClientExecutor>();
            return;
        }

        services.AddKeyedSingleton<IGraphQLClientConfiguration>(
            serviceKey,
            (_, _) => configBuilder.Build()
        );
        services.AddKeyedSingleton<ISchemaProvider>(
            serviceKey,
            (sp, key) =>
                new IntrospectingSchemaProvider(
                    sp.GetRequiredKeyedService<IGraphQLClientConfiguration>(key)
                )
        );
        services.AddKeyedSingleton<IGraphQLClientValidator>(
            serviceKey,
            (sp, key) =>
                new GraphQLClientValidator(sp.GetRequiredKeyedService<ISchemaProvider>(key))
        );
        services.AddKeyedSingleton<IGraphQLClientExecutor>(
            serviceKey,
            (sp, key) =>
                new GraphQLClientExecutor(
                    sp.GetRequiredKeyedService<IGraphQLClientValidator>(key),
                    sp.GetRequiredKeyedService<IGraphQLClientConfiguration>(key),
                    sp.GetService<ILogger<GraphQLClientExecutor>>()
                )
        );
    }

    /// <summary>
    /// Walks the given assemblies, instantiates every <see cref="IGenericGraphQLClientRequest"/>
    /// type without invoking its constructor, and validates each <c>Query</c> against the
    /// schema. Call this after <c>app.Build()</c> to fail fast on schema-incompatible queries.
    ///
    /// For host-startup gating that fails the boot if validation throws, use
    /// <c>builder.UseStartupValidation(...)</c> on the Trax integration package instead.
    /// </summary>
    public static Task ValidateGraphQLClientAssembliesAsync(
        this IServiceProvider services,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        var validator = services.GetRequiredService<IGraphQLClientValidator>();
        return validator.ValidateAssembliesAsync(assemblies);
    }

    /// <summary>
    /// Keyed counterpart of
    /// <see cref="ValidateGraphQLClientAssembliesAsync(IServiceProvider, Assembly[])"/>: validates
    /// the supplied assemblies against the schema of the client registered under
    /// <paramref name="serviceKey"/>. Use this when multiple keyed clients are registered.
    /// </summary>
    public static Task ValidateGraphQLClientAssembliesAsync(
        this IServiceProvider services,
        object serviceKey,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        var validator = services.GetRequiredKeyedService<IGraphQLClientValidator>(serviceKey);
        return validator.ValidateAssembliesAsync(assemblies);
    }
}
