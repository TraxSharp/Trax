using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// The caches have a maximum age, so a change that never reached a node stops being served there
/// within it; and a request is checked against the store only when it names a stored id.
///
/// <para>Enforces <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md"
)]
[TestFixture]
[Category("Integration")]
public class PersistedOperationCacheLifetimeTests
{
    private const string AdrHint =
        "a cached persisted operation is never older than its maximum age "
        + "(Trax.Api docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md)";

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private ServiceProvider _sp = null!;
    private IPersistedOperationStore _store = null!;
    private ManualClock _clock = null!;
    private CountingDataContextFactory _reads = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable.");

        _clock = new ManualClock();
        _sp = await GraphQLFixture.BuildAsync(
            po => po.SingleNode().WithCacheMaxAge(MaxAge),
            services =>
            {
                services.AddSingleton<TimeProvider>(_clock);
                CountingDataContextFactory.Install(services);
            }
        );
        _store = _sp.GetRequiredService<IPersistedOperationStore>();
        _reads = _sp.GetRequiredService<CountingDataContextFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            await _sp.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task AChangeThatNeverReachedTheNode_IsInForceWithinTheMaximumAge()
    {
        var id = $"unbroadcast_{Guid.NewGuid():N}";
        await _store.UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");

        // Deactivated behind this node's back: no invalidation, no broadcast.
        await DeactivateDirectlyAsync(id);
        _clock.Advance(MaxAge - TimeSpan.FromSeconds(1));
        (await ExecuteByIdAsync(id))
            .Should()
            .Contain("\"hello\"", "within the maximum age the node may still serve what it cached");

        _clock.Advance(TimeSpan.FromSeconds(2));

        (await ExecuteByIdAsync(id)).Should().Contain("HC0020", AdrHint);
    }

    [Test]
    public async Task AnInlineRequestUnderRequirePersisted_IsRefusedWithoutReadingTheStore()
    {
        var before = _reads.Count;

        var json = await ExecuteInlineAsync("query Probe { hello }");

        json.Should().Contain("PERSISTED_OPERATION_REQUIRED");
        (_reads.Count - before)
            .Should()
            .Be(
                0,
                "a request that sends its own document is checked against the store only when it names a stored id"
            );
    }

    [Test]
    public async Task AnUnknownIdAskedForTwice_ReadsTheStoreOnce()
    {
        var id = $"unknown_{Guid.NewGuid():N}";
        var before = _reads.Count;

        (await ExecuteByIdAsync(id)).Should().Contain("HC0020");
        (await ExecuteByIdAsync(id)).Should().Contain("HC0020");

        (_reads.Count - before).Should().Be(1, "an id the store does not hold is remembered");
    }

    [Test]
    public async Task AnUnknownIdThatIsThenUploaded_IsServedAtOnce()
    {
        var id = $"late_{Guid.NewGuid():N}";
        (await ExecuteByIdAsync(id)).Should().Contain("HC0020");

        await _store.UpsertAsync(id, "query Greet { hello }", null, CancellationToken.None);

        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"", AdrHint);
    }

    [Test]
    public async Task AnUnknownId_IsReadAgainAfterTheMaximumAge()
    {
        var id = $"unknown_aged_{Guid.NewGuid():N}";
        (await ExecuteByIdAsync(id)).Should().Contain("HC0020");
        var before = _reads.Count;

        _clock.Advance(MaxAge);
        await ExecuteByIdAsync(id);

        (_reads.Count - before).Should().Be(1, AdrHint);
    }

    private async Task<string> ExecuteByIdAsync(string id)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(_sp);
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocumentId(new OperationDocumentId(id)).Build()
        );
        return ((OperationResult)result).ToJson();
    }

    private async Task<string> ExecuteInlineAsync(string document)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(_sp);
        // As the HTTP transports send it: the document, and its hash computed from the bytes.
        var hash = new MD5DocumentHashProvider(HashFormat.Hex).ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(document)
        );
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New().SetDocument(document).SetDocumentHash(hash).Build()
        );
        return ((OperationResult)result).ToJson();
    }

    private static async Task DeactivateDirectlyAsync(string id)
    {
        await using var conn = new NpgsqlConnection(PostgresFixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "update trax.persisted_operation set is_active = false where id = @id",
            conn
        );
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
