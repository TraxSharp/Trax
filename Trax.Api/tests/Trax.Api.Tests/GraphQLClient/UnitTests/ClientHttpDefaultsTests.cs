using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// The client's HTTP and caching defaults follow Microsoft's guidance for a long-lived client:
/// its <see cref="HttpClient"/> comes from <c>IHttpClientFactory</c> under a name per client,
/// so a host adds handlers and resilience to it the standard way; a supplied
/// <see cref="HttpClient"/> is never changed, so one can serve several clients; the validated
/// query cache has a bound; and a schema load that failed is not retried on every request but
/// after a capped, jittered backoff.
/// </summary>
[TestFixture]
public class ClientHttpDefaultsTests
{
    private const string MinimalIntrospection = """
        {"data":{"__schema":{"queryType":{"name":"Query"},"types":[
          {"kind":"OBJECT","name":"Query","fields":[{"name":"n","type":{"kind":"SCALAR","name":"Int"}}]}
        ]}}}
        """;

    [Test]
    public async Task The_default_HttpClient_comes_from_the_factory_under_the_clients_name()
    {
        var seen = new List<Uri?>();
        var services = new ServiceCollection();
        var builder = services.AddKeyedTraxGraphQLClient(
            "billing",
            new Uri("http://billing.test/graphql")
        );
        builder.HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(() =>
            new StubHttpMessageHandler(request =>
            {
                seen.Add(request.RequestUri);
                return Ok(MinimalIntrospection);
            })
        );
        await using var sp = services.BuildServiceProvider();

        var validator = sp.GetRequiredKeyedService<IGraphQLClientValidator>("billing");
        await validator.ValidateAsync("{ n }");

        seen.Should().Equal(new Uri("http://billing.test/graphql"));
        builder
            .HttpClientBuilder.Name.Should()
            .NotBe(
                services
                    .AddTraxGraphQLClient(new Uri("http://other.test/graphql"))
                    .HttpClientBuilder.Name,
                "each client has its own named HttpClient"
            );
    }

    [Test]
    public async Task One_supplied_HttpClient_serves_two_clients_each_at_its_own_address()
    {
        var seen = new List<Uri?>();
        var shared = new HttpClient(
            new StubHttpMessageHandler(request =>
            {
                seen.Add(request.RequestUri);
                return Ok(MinimalIntrospection);
            })
        );
        await shared.GetAsync("http://warm.test/");
        seen.Clear();

        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("a", new Uri("http://a.test/graphql"))
            .ConfigureHttpClient(shared);
        services
            .AddKeyedTraxGraphQLClient("b", new Uri("http://b.test/graphql"))
            .ConfigureHttpClient(shared);
        await using var sp = services.BuildServiceProvider();

        await sp.GetRequiredKeyedService<IGraphQLClientValidator>("a").ValidateAsync("{ n }");
        await sp.GetRequiredKeyedService<IGraphQLClientValidator>("b").ValidateAsync("{ n }");

        seen.Should().Equal(new Uri("http://a.test/graphql"), new Uri("http://b.test/graphql"));
        shared.BaseAddress.Should().BeNull("a supplied HttpClient is not changed");
    }

    [Test]
    public void An_endpoint_in_the_GraphQL_options_that_disagrees_with_the_clients_address_is_refused()
    {
        var builder = new GraphQLClientConfigurationBuilder(new Uri("http://a.test/graphql"));
        builder.GraphQLClientOptions.EndPoint = new Uri("http://b.test/graphql");

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*b.test*a.test*");
    }

    [Test]
    public async Task The_validated_query_cache_has_a_bound()
    {
        var validator = new GraphQLClientValidator(new FixedSchema("type Query { n: Int }"));

        for (var i = 0; i < GraphQLClientValidator.MaxCachedQueries + 50; i++)
            await validator.ValidateAsync($"query Q{i} {{ n }}");

        validator
            .CachedQueries.Count.Should()
            .BeLessThanOrEqualTo(GraphQLClientValidator.MaxCachedQueries);
    }

    [Test]
    public async Task A_failed_load_is_not_retried_until_its_backoff_has_passed()
    {
        var time = new ManualTime();
        var loads = 0;
        var lazy = new RetryingAsyncLazy<int>(
            () =>
            {
                loads++;
                return loads < 3
                    ? Task.FromException<int>(new InvalidOperationException($"load {loads}"))
                    : Task.FromResult(42);
            },
            time
        );

        await lazy.Invoking(l => l.GetValueAsync(default))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        await lazy.Invoking(l => l.GetValueAsync(default))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("load 1", "a request inside the backoff gets the last failure");
        loads.Should().Be(1);

        time.Advance(RetryingAsyncLazy<int>.MaxBackoff);
        await lazy.Invoking(l => l.GetValueAsync(default))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("load 2");

        time.Advance(RetryingAsyncLazy<int>.MaxBackoff);
        (await lazy.GetValueAsync(default)).Should().Be(42);
        loads.Should().Be(3);
    }

    [Test]
    public void The_backoff_grows_with_each_failure_up_to_its_cap_with_jitter()
    {
        var delays = Enumerable.Range(1, 12).Select(RetryingAsyncLazy<int>.Backoff).ToList();

        delays
            .Should()
            .OnlyContain(d => d > TimeSpan.Zero && d <= RetryingAsyncLazy<int>.MaxBackoff);
        delays[0].Should().BeLessThanOrEqualTo(RetryingAsyncLazy<int>.BaseBackoff);
        delays[0].Should().BeGreaterThanOrEqualTo(RetryingAsyncLazy<int>.BaseBackoff / 2);
        delays[^1].Should().BeGreaterThanOrEqualTo(RetryingAsyncLazy<int>.MaxBackoff / 2);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class FixedSchema(string sdl) : ISchemaProvider
    {
        private readonly global::GraphQL.Types.ISchema _schema = global::GraphQL.Types.Schema.For(
            sdl
        );

        public Task<global::GraphQL.Types.ISchema> GetSchemaAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_schema);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
