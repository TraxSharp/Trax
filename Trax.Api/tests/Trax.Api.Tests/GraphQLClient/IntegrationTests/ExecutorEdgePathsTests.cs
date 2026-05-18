using System.Text.Json;
using FluentAssertions;
using GraphQL;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Edge paths in <see cref="GraphQLClientExecutor"/> that the matrix in
/// <see cref="KernelExecutorTests"/> doesn't cover. Each test pins down a specific failure
/// or fallback branch:
/// <list type="bullet">
///   <item>Subscription operation -> NotSupportedException with a clear message.</item>
///   <item>Custom <c>Extract</c> overrides skip strict-shape validation.</item>
///   <item>Server returns multiple GraphQL errors -> all are surfaced in the exception.</item>
///   <item>GraphQLExecutionException's "no errors but failed" branch produces a real message.</item>
/// </list>
/// </summary>
[TestFixture]
public class ExecutorEdgePathsTests
{
    private GraphQLTestServerFixture _fixture = null!;
    private ServiceProvider _services = null!;
    private IGraphQLClientExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new GraphQLTestServerFixture();
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient())
            .WithStrictness(ResponseStrictness.ThrowOnDrift);
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<IGraphQLClientExecutor>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _fixture.Dispose();
    }

    [Test]
    public async Task Run_SubscriptionOperation_ThrowsNotSupportedWithSpecificMessage()
    {
        // Build an executor whose validator pretends every query is a subscription. Lets us
        // exercise the executor's subscription-rejection branch directly without needing the
        // test server to actually accept subscriptions over the test transport.
        var stubValidator = Substitute.For<IGraphQLClientValidator>();
        stubValidator
            .ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GraphQLParser.AST.OperationType.Subscription));

        var subscriptionConfig = _services.GetRequiredService<IGraphQLClientConfiguration>();
        var executor = new GraphQLClientExecutor(stubValidator, subscriptionConfig);

        var act = async () => await executor.Run(new AllItemsRequest());

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("Subscription");
    }

    [Test]
    public async Task Run_CustomExtractor_SkipsStrictShapeValidation()
    {
        // With ThrowOnDrift configured, a default-extractor request with a field-set mismatch
        // would throw. A custom extractor opts out via UsesDefaultExtractor = false; this test
        // proves that path actually skips the shape check.
        var result = await _executor.Run(new CustomExtractorRequest { Id = "player-1" });

        result.UpperName.Should().Be("ARAGORN");
    }

    [Test]
    public void GraphQLExecutionException_NoErrorsButFailed_HasDescriptiveMessage()
    {
        // The two-arg ctor path produces a message when there are no GraphQL errors but
        // execution still failed (e.g. data was null). Without this test, a regression in
        // the message-builder would only show up as a useless "" error string at runtime.
        var ex = new GraphQLExecutionException(
            "data was null",
            new InvalidOperationException("nope")
        );

        ex.Errors.Should().BeEmpty();
        ex.Message.Should().Contain("data was null");
        ex.InnerException.Should().NotBeNull();
    }

    [Test]
    public void GraphQLExecutionException_FromErrors_BuildsAggregateMessage()
    {
        var errors = new GraphQLError[]
        {
            new() { Message = "first" },
            new() { Message = "second" },
        };

        var ex = new GraphQLExecutionException(errors);

        ex.Message.Should().Contain("first");
        ex.Message.Should().Contain("second");
    }

    [Test]
    public void GraphQLExecutionException_EmptyErrorList_StillHasMessage()
    {
        // Defensive: the "no errors" branch in BuildMessage exists so a future caller that
        // passes an empty list doesn't produce a confusing "GraphQL request returned errors:"
        // followed by nothing.
        var ex = new GraphQLExecutionException(Array.Empty<GraphQLError>());

        ex.Message.Should().Contain("no errors but execution failed");
    }
}

/// <summary>
/// Selects fields the POCO can't possibly contain (because the POCO has its own shape),
/// then transforms them via a custom Extract. UsesDefaultExtractor = false tells the
/// executor to skip strict-shape validation, which is the point of this test.
/// </summary>
file sealed class CustomExtractorRequest : IGraphQLClientRequest<UpperNameProjection>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query CustomExtract($id: String!) {
              player(id: $id) { id name level }
            }
            """;

    public object Variables => new { id = Id };

    public bool UsesDefaultExtractor => false;

    public UpperNameProjection Extract(JsonElement data, JsonSerializerOptions options)
    {
        var name = data.GetProperty("player").GetProperty("name").GetString()!;
        return new UpperNameProjection(name.ToUpperInvariant());
    }
}

file sealed record UpperNameProjection(string UpperName);
