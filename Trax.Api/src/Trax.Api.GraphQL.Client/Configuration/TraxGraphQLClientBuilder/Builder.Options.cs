namespace Trax.Api.GraphQL.Client;

public sealed partial class TraxGraphQLClientBuilder
{
    /// <summary>
    /// Sets how strictly the executor checks the JSON response against the request's POCO.
    /// See <see cref="ResponseStrictness"/> for the three modes.
    /// </summary>
    public TraxGraphQLClientBuilder WithStrictness(ResponseStrictness strictness)
    {
        ConfigBuilder.ResponseStrictness = strictness;
        return this;
    }

    /// <summary>
    /// Sends requests through a <see cref="HttpClient"/> you own instead of the one
    /// <c>IHttpClientFactory</c> creates. The client is not changed (requests go to the URI passed
    /// to <see cref="ServiceExtensions.AddTraxGraphQLClient"/>, whatever its <c>BaseAddress</c>), so
    /// one client can serve several GraphQL clients. To add handlers, resilience or timeouts,
    /// prefer <see cref="HttpClientBuilder"/>.
    /// </summary>
    public TraxGraphQLClientBuilder ConfigureHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ConfigBuilder.HttpClient = httpClient;
        return this;
    }

    /// <summary>
    /// Disposes the supplied <see cref="HttpClient"/> when the client configuration is
    /// disposed. Default: false (the consumer owns the client's lifetime).
    /// </summary>
    public TraxGraphQLClientBuilder DisposeHttpClient(bool dispose = true)
    {
        ConfigBuilder.DisposeHttpClient = dispose;
        return this;
    }

    /// <summary>
    /// Replaces the <see cref="System.Text.Json.JsonSerializerOptions"/> used by the
    /// executor's deserializer. Defaults include <c>PropertyNameCaseInsensitive = true</c>
    /// and a snake-case-upper enum converter, both of which the strict-extract validator
    /// relies on. Override with care.
    /// </summary>
    public TraxGraphQLClientBuilder ConfigureJson(System.Text.Json.JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ConfigBuilder.JsonSerializerOptions = options;
        return this;
    }

    /// <summary>
    /// Escape hatch: applies an arbitrary mutation to the underlying configuration builder.
    /// Use this for options not yet surfaced as dedicated <c>With*</c> methods.
    /// </summary>
    public TraxGraphQLClientBuilder Configure(Action<GraphQLClientConfigurationBuilder> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        mutate(ConfigBuilder);
        return this;
    }
}
