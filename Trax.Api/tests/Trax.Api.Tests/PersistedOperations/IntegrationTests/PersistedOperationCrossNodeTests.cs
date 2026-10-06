using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Caching;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Two nodes over one database and one broker. A change made on one node reaches the other
/// node's caches, which HotChocolate does not expire on its own, and a node that may have
/// missed a broadcast empties them.
///
/// <para>Enforces <c>docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md")]
[TestFixture]
[Category("Integration")]
public class PersistedOperationCrossNodeTests
{
    private const string AmqpUri = "amqp://trax:trax123@localhost:5672/";
    private const string AdrHint =
        "a persisted-operation id means one document on every node "
        + "(Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md)";

    // The two nodes meet on an exchange of their own, so another run on the same broker never
    // invalidates their caches.
    private readonly string _exchange = RunExchange.New();

    private ServiceProvider _nodeA = null!;
    private ServiceProvider _nodeB = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Unavailable("Postgres");
        if (!IsRabbitMqReachable())
            Unavailable("RabbitMQ");

        _nodeA = await StartNodeAsync();
        _nodeB = await StartNodeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        foreach (var node in new[] { _nodeA, _nodeB })
        {
            if (node is null)
                continue;
            await node.GetRequiredService<PersistedOperationReceiverService>()
                .StopAsync(CancellationToken.None);
            await node.DisposeAsync();
        }
        await RunExchange.DeleteAsync(AmqpUri, _exchange);
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task ADeactivationOnOneNode_IsRefusedOnTheOther()
    {
        var id = $"cross_node_deactivate_{Guid.NewGuid():N}";
        await Store(_nodeA).UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        (await ExecuteByIdAsync(_nodeB, id)).Should().Contain("\"hello\"", "B warms its caches");

        await Store(_nodeA).DeactivateAsync(id, null, "retired", CancellationToken.None);

        (await EventuallyAsync(_nodeB, id, json => !json.Contains("\"hello\"")))
            .Should()
            .NotContain("\"hello\"", AdrHint);
    }

    [Test]
    public async Task AReuploadOnOneNode_IsServedOnTheOther()
    {
        var id = $"cross_node_reupload_{Guid.NewGuid():N}";
        await Store(_nodeA).UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        (await ExecuteByIdAsync(_nodeB, id)).Should().Contain("\"hello\"");

        await Store(_nodeA)
            .UpsertAsync(
                id,
                "query Greet { version }",
                new UpsertOptions { BypassShapeDiff = true },
                CancellationToken.None
            );

        (await EventuallyAsync(_nodeB, id, json => json.Contains("\"version\"")))
            .Should()
            .Contain("\"version\"", AdrHint);
    }

    [Test]
    public async Task ADeactivationOnOneNode_DuringARequestOnTheOther_StaysInForceThere()
    {
        var id = $"cross_node_held_{Guid.NewGuid():N}";
        var held = GraphQLFixture.Hold(id);
        await Store(_nodeA)
            .UpsertAsync(id, GraphQLFixture.HeldDocument(id), null, CancellationToken.None);
        var generationOnB = _nodeB.GetRequiredService<PersistedOperationCacheGeneration>();

        var running = ExecuteByIdAsync(_nodeB, id);
        await held.Entered;
        var before = generationOnB.Current;
        await Store(_nodeA).DeactivateAsync(id, null, "retired", CancellationToken.None);
        await WaitUntilAsync(() => generationOnB.Current > before);
        held.Release();
        await running;

        (await ExecuteByIdAsync(_nodeB, id)).Should().Contain("HC0020", AdrHint);
    }

    [Test]
    public async Task ANodeThatMayHaveMissedABroadcast_EmptiesItsCaches()
    {
        var id = $"cross_node_missed_{Guid.NewGuid():N}";
        await Store(_nodeB).UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        await ExecuteByIdAsync(_nodeB, id);
        var executor = await GraphQLFixture.GetExecutorAsync(_nodeB);
        executor
            .Schema.Services.GetRequiredService<IDocumentCache>()
            .Count.Should()
            .BeGreaterThan(0);

        // What the receiver does when its broker connection drops or recovers.
        await _nodeB
            .GetRequiredService<PersistedOperationReceiverService>()
            .EmptyEveryCacheAsync("a test");

        executor.Schema.Services.GetRequiredService<IDocumentCache>().Count.Should().Be(0, AdrHint);
        executor
            .Schema.Services.GetRequiredService<IPreparedOperationCache>()
            .Count.Should()
            .Be(0, AdrHint);
    }

    private async Task<ServiceProvider> StartNodeAsync()
    {
        var node = await GraphQLFixture.BuildAsync(po => po.UseRabbitMqInvalidation(AmqpUri));
        node.GetRequiredService<PersistedOperationsOptions>().RabbitMqExchange = _exchange;
        await node.GetRequiredService<PersistedOperationReceiverService>()
            .StartAsync(CancellationToken.None);
        // Build the executor now, so the invalidator knows the schema before any broadcast.
        await GraphQLFixture.GetExecutorAsync(node);
        return node;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            // determinism: polling for a broker-delivered broadcast, bounded by the deadline above.
            await Task.Delay(20);
        condition().Should().BeTrue("the broadcast should arrive within the deadline");
    }

    private static IPersistedOperationStore Store(IServiceProvider node) =>
        node.GetRequiredService<IPersistedOperationStore>();

    private static async Task<string> EventuallyAsync(
        IServiceProvider node,
        string id,
        Func<string, bool> done
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        string json;
        do
        {
            json = await ExecuteByIdAsync(node, id);
            if (done(json))
                return json;
            // determinism: polling for a broker-delivered broadcast, bounded by the deadline above.
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        return json;
    }

    private static async Task<string> ExecuteByIdAsync(IServiceProvider node, string id)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(node);
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocumentId(new OperationDocumentId(id)).Build()
        );
        return ((OperationResult)result).ToJson();
    }

    /// <summary>
    /// Skips the fixture on a developer machine without the service, and fails it in CI, where
    /// both services are declared: a skipped run there would report green without ever
    /// checking that a change on one node reaches the other.
    /// </summary>
    private static void Unavailable(string service)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail(
                $"{service} is not reachable, and CI declares it for this fixture. {AdrHint}"
            );
        Assert.Ignore($"{service} not reachable.");
    }

    private static bool IsRabbitMqReachable()
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
            using var conn = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            return conn.IsOpen;
        }
        catch
        {
            return false;
        }
    }
}
