using System.Net;
using AwesomeAssertions;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// A schema provider loads once and shares the result, but a load that failed is not kept:
/// once its backoff has passed the next request loads again, so a server that was briefly
/// unreachable at boot does not leave the client unable to validate anything until the process
/// restarts. A caller's
/// cancellation token stops that caller's wait without cancelling the shared load.
/// </summary>
[TestFixture]
public class SchemaProviderRetryTests
{
    private const string MinimalIntrospection = """
        {
          "data": {
            "__schema": {
              "queryType": { "name": "Query" },
              "types": [
                { "kind": "OBJECT", "name": "Query", "fields": [{ "name": "n", "type": { "kind": "SCALAR", "name": "Int" } }] }
              ]
            }
          }
        }
        """;

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static IGraphQLClientConfiguration Config(HttpMessageHandler handler) =>
        new GraphQLClientConfigurationBuilder(new Uri("http://stub/graphql"))
        {
            HttpClient = new HttpClient(handler),
        }.Build();

    [Test]
    public async Task Introspection_that_failed_once_is_retried_on_the_next_request()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
            ++calls == 1
                ? throw new HttpRequestException("connection refused")
                : Ok(MinimalIntrospection)
        );
        var time = new ManualTime();
        var provider = new IntrospectingSchemaProvider(Config(handler), time);

        await provider
            .Invoking(p => p.GetSchemaAsync())
            .Should()
            .ThrowAsync<GraphQLSchemaIntrospectionException>();

        time.PassBackoff();
        var schema = await provider.GetSchemaAsync();

        schema.Query.Should().NotBeNull();
        calls.Should().Be(2);
    }

    [Test]
    public async Task A_loaded_schema_is_shared_and_not_loaded_again()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return Ok(MinimalIntrospection);
        });
        var provider = new IntrospectingSchemaProvider(Config(handler));

        var first = await provider.GetSchemaAsync();
        var second = await provider.GetSchemaAsync();

        second.Should().BeSameAs(first);
        calls.Should().Be(1);
    }

    [Test]
    public async Task An_already_cancelled_token_is_honoured_before_any_load()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return Ok(MinimalIntrospection);
        });
        var provider = new IntrospectingSchemaProvider(Config(handler));

        await provider
            .Invoking(p => p.GetSchemaAsync(new CancellationToken(canceled: true)))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        calls.Should().Be(0);
    }

    [Test]
    public async Task Cancelling_one_waiter_does_not_cancel_the_shared_load()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BlockingHandler(release.Task, MinimalIntrospection);
        var provider = new IntrospectingSchemaProvider(Config(handler));

        using var cts = new CancellationTokenSource();
        var cancelled = provider.GetSchemaAsync(cts.Token);
        var patient = provider.GetSchemaAsync();
        await cts.CancelAsync();

        try
        {
            // Bounded so a provider that ignores the token fails here instead of hanging.
            await FluentActions
                .Awaiting(() => cancelled.WaitAsync(TimeSpan.FromSeconds(10)))
                .Should()
                .ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.TrySetResult();
        }

        var schema = await patient.WaitAsync(TimeSpan.FromSeconds(10));

        schema.Query.Should().NotBeNull();
        handler.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_schema_file_missing_at_first_is_read_once_it_exists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trax-retry-{Guid.NewGuid():N}.graphql");
        try
        {
            var time = new ManualTime();
            var provider = new FileSchemaProvider(path, removeSubscriptionsFromSchema: true, time);

            await provider
                .Invoking(p => p.GetSchemaAsync())
                .Should()
                .ThrowAsync<GraphQLSchemaIntrospectionException>();

            time.PassBackoff();
            await File.WriteAllTextAsync(path, "type Query { n: Int }");
            var schema = await provider.GetSchemaAsync();

            schema.Query.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task An_assembly_schema_that_failed_to_build_once_is_built_again()
    {
        var attempts = 0;
        var time = new ManualTime();
        var provider = new AssemblySchemaProvider(
            b =>
            {
                if (++attempts == 1)
                    throw new InvalidOperationException("first build fails");
                b.AddQueryType<TestQuery>();
            },
            removeSubscriptionsFromSchema: true,
            time
        );

        await provider
            .Invoking(p => p.GetSchemaAsync())
            .Should()
            .ThrowAsync<InvalidOperationException>();

        time.PassBackoff();

        var schema = await provider.GetSchemaAsync();

        schema.Query.Should().NotBeNull();
        attempts.Should().Be(2);
    }

    private sealed class BlockingHandler(Task gate, string body) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _calls);
            await gate.ConfigureAwait(false);
            return Ok(body);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void PassBackoff() => _now += RetryingAsyncLazy<object>.MaxBackoff;
    }
}
