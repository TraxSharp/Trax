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
    /// Replace the underlying <see cref="HttpClient"/>. Use this to attach authentication
    /// handlers, logging delegates, custom timeouts, etc. The supplied client's
    /// <c>BaseAddress</c> is overwritten with the URI passed to
    /// <see cref="ServiceExtensions.AddTraxGraphQLClient"/>.
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
