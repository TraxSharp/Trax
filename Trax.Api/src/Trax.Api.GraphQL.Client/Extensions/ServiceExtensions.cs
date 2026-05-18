using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

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

        // The configuration is built lazily so that chained method calls on the returned
        // builder can mutate the underlying state before DI resolves the singleton.
        services.AddSingleton<IGraphQLClientConfiguration>(_ => configBuilder.Build());
        services.AddSingleton<ISchemaProvider, IntrospectingSchemaProvider>();
        services.AddSingleton<IGraphQLClientValidator, GraphQLClientValidator>();
        services.AddSingleton<IGraphQLClientExecutor, GraphQLClientExecutor>();

        return new TraxGraphQLClientBuilder(services, configBuilder);
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
}
