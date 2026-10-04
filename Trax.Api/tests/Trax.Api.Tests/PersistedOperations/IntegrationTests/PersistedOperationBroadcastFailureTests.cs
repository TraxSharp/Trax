using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// A change that did not reach every node says so: the mutation returns the saved operation with
/// a <c>CHANGE_NOT_BROADCAST</c> error rather than success, and a node that was not told stops
/// serving what it cached within the cache's maximum age.
///
/// <para>Enforces <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md"
)]
[TestFixture]
[Category("Integration")]
public class PersistedOperationBroadcastFailureTests
{
    private const string AdrHint =
        "a change that did not reach every node says so "
        + "(Trax.Api docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md)";

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private ServiceProvider _changingNode = null!;
    private ServiceProvider _otherNode = null!;
    private ManualClock _otherClock = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable.");

        _changingNode = await GraphQLFixture.BuildAsync(
            po => po.SingleNode(),
            services =>
                services.AddSingleton<IPersistedOperationBroadcaster>(new UnconfirmedBroadcaster())
        );
        _otherClock = new ManualClock();
        _otherNode = await GraphQLFixture.BuildAsync(
            po => po.SingleNode().WithCacheMaxAge(MaxAge),
            services => services.AddSingleton<TimeProvider>(_otherClock)
        );
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_changingNode is not null)
            await _changingNode.DisposeAsync();
        if (_otherNode is not null)
            await _otherNode.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task ADeactivationThatWasNotBroadcast_ReturnsTheSavedOperationAndAnError()
    {
        var id = $"unconfirmed_{Guid.NewGuid():N}";
        await _otherNode
            .GetRequiredService<IPersistedOperationStore>()
            .UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        (await ExecuteByIdAsync(_otherNode, id)).Should().Contain("\"hello\"");

        var payload = await MutateAsync(
            "deactivatePersistedOperation",
            "DeactivatePersistedOperationInput",
            new Dictionary<string, object?> { ["id"] = id, ["reason"] = "retired" }
        );

        payload.GetProperty("success").GetBoolean().Should().BeFalse(AdrHint);
        payload.GetProperty("operation").GetProperty("isActive").GetBoolean().Should().BeFalse();
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("CHANGE_NOT_BROADCAST", AdrHint);

        // The other node was not told; its cache's maximum age is the backstop.
        (await ExecuteByIdAsync(_otherNode, id))
            .Should()
            .Contain("\"hello\"");
        _otherClock.Advance(MaxAge);
        (await ExecuteByIdAsync(_otherNode, id)).Should().Contain("HC0020", AdrHint);
    }

    [Test]
    public async Task AnUploadThatWasNotBroadcast_ReturnsTheSavedOperationAndAnError()
    {
        var id = $"unconfirmed_upload_{Guid.NewGuid():N}";

        var payload = await MutateAsync(
            "uploadPersistedOperation",
            "UploadPersistedOperationInput",
            new Dictionary<string, object?> { ["id"] = id, ["document"] = "query Greet { hello }" }
        );

        payload.GetProperty("success").GetBoolean().Should().BeFalse(AdrHint);
        payload.GetProperty("operation").GetProperty("id").GetString().Should().Be(id);
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("CHANGE_NOT_BROADCAST", AdrHint);
    }

    [Test]
    public async Task ARestoreThatWasNotBroadcast_ReturnsTheSavedOperationAndAnError()
    {
        var id = $"unconfirmed_restore_{Guid.NewGuid():N}";
        var store = _otherNode.GetRequiredService<IPersistedOperationStore>();
        await store.UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        await store.DeactivateAsync(id, null, "retired", CancellationToken.None);

        var payload = await MutateAsync(
            "restorePersistedOperation",
            "RestorePersistedOperationInput",
            new Dictionary<string, object?> { ["id"] = id }
        );

        payload.GetProperty("success").GetBoolean().Should().BeFalse(AdrHint);
        payload.GetProperty("operation").GetProperty("isActive").GetBoolean().Should().BeTrue();
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("CHANGE_NOT_BROADCAST", AdrHint);
    }

    private async Task<JsonElement> MutateAsync(
        string field,
        string inputType,
        Dictionary<string, object?> input
    )
    {
        var executor = await GraphQLFixture.GetExecutorAsync(_changingNode);
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(
                    $$"""
                    mutation Change($input: {{inputType}}!) {
                      operations {
                        persistedOperations {
                          {{field}}(input: $input) {
                            success
                            operation { id isActive }
                            errors { code message }
                          }
                        }
                      }
                    }
                    """
                )
                .SetVariableValues(new Dictionary<string, object?> { ["input"] = input })
                .Build()
        );
        var json = JsonDocument.Parse(((OperationResult)result).ToJson());
        return json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty(field);
    }

    private static async Task<string> ExecuteByIdAsync(IServiceProvider node, string id)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(node);
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocumentId(new OperationDocumentId(id)).Build()
        );
        return ((OperationResult)result).ToJson();
    }

    /// <summary>A broker that never confirms what it was sent.</summary>
    private sealed class UnconfirmedBroadcaster : IPersistedOperationBroadcaster
    {
        public Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct) =>
            Task.FromException(new IOException("The broker did not confirm the publish."));
    }
}
