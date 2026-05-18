using System.Text.Json;
using FluentAssertions;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// Direct tests of <see cref="GraphQLClientConfiguration"/>'s constructor and Dispose
/// behavior. The constructor's null-arg guards exist so misconfiguration fails at startup
/// rather than producing a NullReferenceException on the first request. Dispose has two
/// branches (DisposeHttpClient on/off) that need to be exercised.
/// </summary>
[TestFixture]
public class ConfigurationLifecycleTests
{
    [Test]
    public void Ctor_NullBaseAddress_Throws()
    {
        var act = () =>
            new GraphQLClientConfiguration(
                baseAddress: null!,
                jsonSerializer: new SystemTextJsonSerializer(),
                graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
                jsonSerializerOptions: new JsonSerializerOptions(),
                disposeHttpClient: false,
                removeSubscriptionsFromSchema: true,
                responseStrictness: ResponseStrictness.Lenient,
                httpClient: new HttpClient()
            );
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Ctor_NullJsonSerializer_Throws()
    {
        var act = () =>
            new GraphQLClientConfiguration(
                baseAddress: new Uri("http://x/graphql"),
                jsonSerializer: null!,
                graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
                jsonSerializerOptions: new JsonSerializerOptions(),
                disposeHttpClient: false,
                removeSubscriptionsFromSchema: true,
                responseStrictness: ResponseStrictness.Lenient,
                httpClient: new HttpClient()
            );
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Ctor_NullHttpClient_Throws()
    {
        var act = () =>
            new GraphQLClientConfiguration(
                baseAddress: new Uri("http://x/graphql"),
                jsonSerializer: new SystemTextJsonSerializer(),
                graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
                jsonSerializerOptions: new JsonSerializerOptions(),
                disposeHttpClient: false,
                removeSubscriptionsFromSchema: true,
                responseStrictness: ResponseStrictness.Lenient,
                httpClient: null!
            );
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Dispose_DisposeHttpClientFalse_DoesNotDisposeClient()
    {
        var http = new HttpClient();
        var config = new GraphQLClientConfiguration(
            baseAddress: new Uri("http://x/graphql"),
            jsonSerializer: new SystemTextJsonSerializer(),
            graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
            jsonSerializerOptions: new JsonSerializerOptions(),
            disposeHttpClient: false,
            removeSubscriptionsFromSchema: true,
            responseStrictness: ResponseStrictness.Lenient,
            httpClient: http
        );

        config.Dispose();

        // If the client had been disposed, this would throw ObjectDisposedException.
        var act = () => http.BaseAddress = new Uri("http://other/graphql");
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_DisposeHttpClientTrue_DisposesClient()
    {
        var http = new HttpClient();
        var config = new GraphQLClientConfiguration(
            baseAddress: new Uri("http://x/graphql"),
            jsonSerializer: new SystemTextJsonSerializer(),
            graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
            jsonSerializerOptions: new JsonSerializerOptions(),
            disposeHttpClient: true,
            removeSubscriptionsFromSchema: true,
            responseStrictness: ResponseStrictness.Lenient,
            httpClient: http
        );

        config.Dispose();

        var act = () => http.GetAsync("http://x/y").GetAwaiter().GetResult();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var config = new GraphQLClientConfiguration(
            baseAddress: new Uri("http://x/graphql"),
            jsonSerializer: new SystemTextJsonSerializer(),
            graphQLHttpClientOptions: new GraphQLHttpClientOptions(),
            jsonSerializerOptions: new JsonSerializerOptions(),
            disposeHttpClient: true,
            removeSubscriptionsFromSchema: true,
            responseStrictness: ResponseStrictness.Lenient,
            httpClient: new HttpClient()
        );

        config.Dispose();
        var act = () => config.Dispose();

        act.Should().NotThrow();
    }
}
