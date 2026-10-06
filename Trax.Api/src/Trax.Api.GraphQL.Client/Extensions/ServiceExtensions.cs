using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Registers the Trax GraphQL client kernel, which validates outbound queries against the server's
/// schema and runs them. Start with <c>AddTraxGraphQLClient</c>, then resolve
/// <see cref="IGraphQLClientExecutor"/>.
/// </summary>
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
        var http = Register(services, configBuilder, serviceKey: null);
        return new TraxGraphQLClientBuilder(services, configBuilder, serviceKey: null, http);
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
        var http = Register(services, configBuilder, serviceKey);
        return new TraxGraphQLClientBuilder(services, configBuilder, serviceKey, http);
    }

    /// <summary>
    /// The name of the <c>IHttpClientFactory</c> client a GraphQL client sends through. A keyed
    /// client's name carries its key's type as well as its value, so the string key
    /// <c>"Billing"</c> and an enum value <c>Billing</c> get different clients.
    /// </summary>
    internal static string HttpClientName(object? serviceKey) =>
        serviceKey is null
            ? "Trax.Api.GraphQL.Client"
            : $"Trax.Api.GraphQL.Client:{serviceKey.GetType().FullName}:{serviceKey}";

    /// <summary>
    /// Registers the four client services either unkeyed (<paramref name="serviceKey"/> is
    /// <c>null</c>) or keyed, and the named HttpClient they send through. Keyed registrations
    /// resolve their dependencies by the same key, because Microsoft DI does not cascade the key to
    /// a service's own constructor arguments. The configuration is built lazily so chained builder
    /// calls can mutate state before DI resolves the singleton.
    /// </summary>
    /// <remarks>
    /// The configuration is a singleton that keeps its HttpClient, so the factory's handler
    /// rotation never reaches it. Following Microsoft's guidance for a long-lived client from the
    /// factory, the primary handler is a <see cref="SocketsHttpHandler"/> with a
    /// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>, and the factory's handler
    /// lifetime is infinite.
    /// </remarks>
    private static IHttpClientBuilder Register(
        IServiceCollection services,
        GraphQLClientConfigurationBuilder configBuilder,
        object? serviceKey
    )
    {
        var httpClientName = HttpClientName(serviceKey);
        var http = services
            .AddHttpClient(httpClientName)
            .UseSocketsHttpHandler(
                (handler, _) =>
                    handler.PooledConnectionLifetime =
                        GraphQLClientConfigurationBuilder.PooledConnectionLifetime
            )
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        IGraphQLClientConfiguration Configuration(IServiceProvider sp) =>
            configBuilder.Build(() =>
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName)
            );

        if (serviceKey is null)
        {
            services.AddSingleton<IGraphQLClientConfiguration>(Configuration);
            services.AddSingleton<ISchemaProvider, IntrospectingSchemaProvider>();
            services.AddSingleton<IGraphQLClientValidator, GraphQLClientValidator>();
            services.AddSingleton<IGraphQLClientExecutor, GraphQLClientExecutor>();
            return http;
        }

        services.AddKeyedSingleton<IGraphQLClientConfiguration>(
            serviceKey,
            (sp, _) => Configuration(sp)
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
        return http;
    }

    /// <summary>
    /// Walks the given assemblies, instantiates every <see cref="IGenericGraphQLClientRequest"/>
    /// type that carries no <see cref="GraphQLClientAttribute"/> without invoking its constructor,
    /// and validates each <c>Query</c> against the unkeyed client's schema. A request marked for a
    /// keyed client is left to that client. Call this after <c>app.Build()</c> to fail fast on
    /// schema-incompatible queries.
    ///
    /// For host-startup gating that fails the boot if validation throws, use
    /// <c>builder.UseStartupValidation(...)</c> on the Trax integration package instead.
    /// </summary>
    /// <exception cref="GraphQLValidationException">A request's query is not valid against the schema.</exception>
    /// <exception cref="InvalidOperationException">The assemblies hold no unmarked request type, or a request marked with a key no client is registered under.</exception>
    public static Task ValidateGraphQLClientAssembliesAsync(
        this IServiceProvider services,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        var validator = services.GetRequiredService<IGraphQLClientValidator>();
        return validator.ValidateClientRequestsAsync(
            assemblies,
            serviceKey: null,
            IsRegisteredClientKey(services),
            CancellationToken.None
        );
    }

    /// <summary>
    /// Keyed counterpart of
    /// <see cref="ValidateGraphQLClientAssembliesAsync(IServiceProvider, Assembly[])"/>: validates
    /// the request types marked <c>[GraphQLClient(serviceKey)]</c> against the schema of the
    /// client registered under <paramref name="serviceKey"/>. Use this when multiple keyed
    /// clients are registered; requests for the other servers can share the assemblies.
    /// </summary>
    /// <exception cref="GraphQLValidationException">A request's query is not valid against the schema.</exception>
    /// <exception cref="InvalidOperationException">No request type is marked with <paramref name="serviceKey"/>, no client is registered under it, or a request is marked with a key no client is registered under.</exception>
    public static Task ValidateGraphQLClientAssembliesAsync(
        this IServiceProvider services,
        object serviceKey,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(assemblies);
        var validator = services.GetRequiredKeyedService<IGraphQLClientValidator>(serviceKey);
        return validator.ValidateClientRequestsAsync(
            assemblies,
            serviceKey,
            IsRegisteredClientKey(services),
            CancellationToken.None
        );
    }

    /// <summary>
    /// Whether a keyed GraphQL client is registered under a key in <paramref name="services"/>.
    /// </summary>
    internal static Func<object, bool> IsRegisteredClientKey(IServiceProvider services) =>
        key => services.GetKeyedService<IGraphQLClientValidator>(key) is not null;
}
