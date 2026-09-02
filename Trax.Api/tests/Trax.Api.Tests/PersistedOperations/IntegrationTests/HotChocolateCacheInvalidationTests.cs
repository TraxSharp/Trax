using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Caching;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Reproduces the hot-fix regression described in
/// <c>PERSISTED_OPS_HOTFIX_BUG.md</c>: re-uploading a shape-bypassed edit
/// to an existing persisted operation must serve the new document on the
/// next request without a process restart.
/// </summary>
/// <remarks>
/// Before the fix, HotChocolate's parsed-document cache (keyed by persisted-op id) and
/// prepared-operation cache (keyed by
/// <c>{schema}-{executorVersion}-{documentId}+{operationName}</c>) held on to the prior
/// document's compiled form even after <c>IOperationDocumentStorage.TryReadAsync</c>
/// returned the new text. Both caches live in the executor's service provider, so Trax
/// invalidates them by evicting the executor; the tests assert the observable
/// consequences — a new executor version, and the new document being served.
/// <para>
/// Every execute resolves the executor from DI rather than reusing a captured one,
/// mirroring the ASP.NET Core endpoint, which does the same per request. A captured
/// executor keeps its own (now stale) caches and would never see an eviction.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
public class HotChocolateCacheInvalidationTests
{
    // Distinct documents that produce different response keys ("hello" vs
    // "version"). The Greet operation name keeps the cache key stable
    // across the two upserts.
    private const string DocHello = "query Greet { hello }";
    private const string DocVersion = "query Greet { version }";

    private ServiceProvider _sp = null!;
    private IPersistedOperationStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping HC cache invalidation tests.");

        _sp = await GraphQLFixture.BuildAsync();
        _store = _sp.GetRequiredService<IPersistedOperationStore>();
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
    public async Task Upsert_AfterFirstExecute_ServesNewDocument_OnNextExecute()
    {
        const string id = "hotfix_via_store_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");

        await _store.UpsertAsync(
            id,
            DocVersion,
            new UpsertOptions { BypassShapeDiff = true },
            CancellationToken.None
        );

        var second = await ExecuteByIdAsync(id);
        second.Should().Contain("\"version\"", "the new document must take effect immediately");
        second
            .Should()
            .NotContain(
                "\"hello\"",
                "the prior document must NOT still be served from HC's cache layers"
            );
    }

    [Test]
    public async Task Upsert_ViaGraphQLMutation_AlsoInvalidatesHCCache()
    {
        const string id = "hotfix_via_mutation_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");

        await ExecuteUploadMutationAsync(id, DocVersion, bypassShapeDiff: true);

        var after = await ExecuteByIdAsync(id);
        after.Should().Contain("\"version\"");
        after.Should().NotContain("\"hello\"");
    }

    [Test]
    public async Task Upsert_EmptiesBothCaches()
    {
        const string id = "cache_empty_on_upsert_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        await ExecuteByIdAsync(id);

        var (documents, operations) = await CachesAsync();
        documents.Count.Should().BeGreaterThan(0, "the warm execute must have populated it");
        operations.Count.Should().BeGreaterThan(0, "the warm execute must have compiled it");

        await _store.UpsertAsync(
            id,
            DocVersion,
            new UpsertOptions { BypassShapeDiff = true },
            CancellationToken.None
        );

        documents.Count.Should().Be(0, "Upsert must drop the parsed document");
        operations.Count.Should().Be(0, "Upsert must drop the compiled operation");
    }

    [Test]
    public async Task Deactivate_InvalidatesCache_SoStaleDocumentIsNotServed()
    {
        const string id = "deact_cache_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");

        await _store.DeactivateAsync(id, null, "test", CancellationToken.None);

        // After deactivation the storage returns null. If the HC caches
        // weren't cleared the prior compiled operation would still serve.
        // The persisted-operation pipeline rejects when no document is
        // found OR when it is not active.
        var json = await ExecuteByIdAsync(id);
        json.Should()
            .NotContain(
                "\"hello\"",
                "deactivated operation must not be served from the prepared-op cache"
            );
    }

    [Test]
    public async Task Restore_InvalidatesCache_AndRestoredDocumentRuns()
    {
        const string id = "restore_cache_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        await _store.DeactivateAsync(id, null, "test", CancellationToken.None);

        // Confirm the executor cannot serve while deactivated.
        (await ExecuteByIdAsync(id))
            .Should()
            .NotContain("\"hello\"");

        await _store.RestoreAsync(id, null, CancellationToken.None);

        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");
    }

    [Test]
    public async Task Upsert_WithoutBypass_AndShapePreservingChange_StillSwapsDocument()
    {
        // A genuinely different document text with an identical fingerprint
        // (same response keys, same selection structure). Equivalent to the
        // "rewrite" case the bug report calls out: shape preserved, body
        // changed, no bypass needed.
        const string id = "shapepreserve_v1";
        const string original = "query Greet { hello }";
        const string rewrite = "query Greet { hello # rewritten\n}";

        await _store.UpsertAsync(id, original, null, CancellationToken.None);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");

        await _store.UpsertAsync(id, rewrite, null, CancellationToken.None);

        var stored = await _store.GetAsync(id, null, CancellationToken.None);
        stored!.Document.Should().Be(rewrite, "the rewritten document must be persisted");

        // The response shape is unchanged, so the observable consequence is that the
        // cached compiled form was dropped and the rewritten document still runs.
        var (documents, operations) = await CachesAsync();
        documents.Count.Should().Be(0);
        operations.Count.Should().Be(0);
        (await ExecuteByIdAsync(id)).Should().Contain("\"hello\"");
    }

    [Test]
    public async Task Invalidator_EmptiesBothCaches_Directly()
    {
        // Direct exercise of the invalidator from root services. Pins the contract that
        // both cache layers are reachable from it, not just one of them.
        const string id = "invalidator_v1";
        await _store.UpsertAsync(id, DocHello, null, CancellationToken.None);
        await ExecuteByIdAsync(id);

        var (documents, operations) = await CachesAsync();
        documents.Count.Should().BeGreaterThan(0);
        operations.Count.Should().BeGreaterThan(0);

        var invalidator = _sp.GetRequiredService<HotChocolateOperationCacheInvalidator>();
        await invalidator.InvalidateAsync(CancellationToken.None);

        documents.Count.Should().Be(0);
        operations.Count.Should().Be(0);
    }

    /// <summary>
    /// The substituted caches out of the executor's own service provider. Trax replaces
    /// HotChocolate's implementations with clearable ones; anything else here means the
    /// substitution stopped being wired and invalidation is silently a no-op.
    /// </summary>
    private async Task<(IDocumentCache Documents, IPreparedOperationCache Operations)> CachesAsync()
    {
        var services = (await GraphQLFixture.GetExecutorAsync(_sp)).Schema.Services;

        var documents = services.GetRequiredService<IDocumentCache>();
        var operations = services.GetRequiredService<IPreparedOperationCache>();

        documents.Should().BeOfType<ClearableDocumentCache>();
        operations.Should().BeOfType<ClearablePreparedOperationCache>();

        return (documents, operations);
    }

    [Test]
    public async Task Invalidator_PreCancelledToken_Throws()
    {
        var invalidator = _sp.GetRequiredService<HotChocolateOperationCacheInvalidator>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => invalidator.InvalidateAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task<string> ExecuteByIdAsync(string id)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(_sp);
        var request = OperationRequestBuilder
            .New()
            .SetDocumentId(new OperationDocumentId(id))
            .Build();
        var result = await executor.ExecuteAsync(request);
        var op = result as OperationResult;
        op.Should().NotBeNull();
        return op!.ToJson();
    }

    private async Task ExecuteUploadMutationAsync(string id, string document, bool bypassShapeDiff)
    {
        const string mutation = """
            mutation Upload($input: UploadPersistedOperationInput!) {
              operations {
                persistedOperations {
                  uploadPersistedOperation(input: $input) {
                    success
                    errors { code message }
                  }
                }
              }
            }
            """;
        var input = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["document"] = document,
            ["bypassShapeDiff"] = bypassShapeDiff,
        };
        var executor = await GraphQLFixture.GetExecutorAsync(_sp);
        var request = OperationRequestBuilder
            .New()
            .SetDocument(mutation)
            .SetVariableValues(new Dictionary<string, object?> { ["input"] = input })
            .Build();
        var result = await executor.ExecuteAsync(request);
        var op = (OperationResult)result;
        op.Errors.Should().BeNullOrEmpty();
        op.ToJson().Should().Contain("\"success\": true");
    }
}
