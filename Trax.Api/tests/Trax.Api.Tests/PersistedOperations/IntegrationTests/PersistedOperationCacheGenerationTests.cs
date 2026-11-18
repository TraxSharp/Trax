using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Core.Extensions;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// A request that read a persisted operation before a change and finishes after it never puts
/// what it read back into the caches: a cache entry older than the last invalidation is never
/// served.
///
/// <para>Enforces <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md"
)]
[TestFixture]
[Category("Integration")]
public class PersistedOperationCacheGenerationTests
{
    private const string AdrHint =
        "a cached persisted operation is never older than the last change "
        + "(Trax.Api docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md)";

    private ServiceProvider? _sp;
    private IPersistedOperationStore? _store;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable.");

        _sp = await GraphQLFixture.BuildAsync();
        _store = _sp.NotNull().GetRequiredService<IPersistedOperationStore>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            if (_sp is not null)
                await _sp.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task ADeactivationDuringARequest_StaysInForceAfterTheRequestFinishes()
    {
        var id = $"held_deactivate_{Guid.NewGuid():N}";
        var held = GraphQLFixture.Hold(id);
        await _store
            .NotNull()
            .UpsertAsync(
                id,
                GraphQLFixture.HeldDocument(id),
                options: null,
                CancellationToken.None
            );

        var running = ExecuteByIdAsync(_sp.NotNull(), id);
        await held.Entered;
        await _store
            .NotNull()
            .DeactivateAsync(id, tenantKey: null, "retired", CancellationToken.None);
        held.Release();
        (await running).Should().Contain("\"held\"", "the request had already started");

        var after = await ExecuteByIdAsync(_sp.NotNull(), id);

        after.Should().Contain("HC0020", AdrHint);
        after.Should().NotContain("\"held\"", AdrHint);
    }

    [Test]
    public async Task AReuploadDuringARequest_IsWhatTheNextRequestRuns()
    {
        var id = $"held_reupload_{Guid.NewGuid():N}";
        var held = GraphQLFixture.Hold(id);
        await _store
            .NotNull()
            .UpsertAsync(
                id,
                GraphQLFixture.HeldDocument(id),
                options: null,
                CancellationToken.None
            );

        var running = ExecuteByIdAsync(_sp.NotNull(), id);
        await held.Entered;
        await _store
            .NotNull()
            .UpsertAsync(
                id,
                "query Held { version }",
                new UpsertOptions { BypassShapeDiff = true },
                CancellationToken.None
            );
        held.Release();
        await running;

        var after = await ExecuteByIdAsync(_sp.NotNull(), id);

        after.Should().Contain("\"version\"", AdrHint);
    }

    internal static async Task<string> ExecuteByIdAsync(IServiceProvider sp, string id)
    {
        var executor = await GraphQLFixture.GetExecutorAsync(sp).ConfigureAwait(false);
        var result = await executor
            .ExecuteAsync(
                OperationRequestBuilder.New().SetDocumentId(new OperationDocumentId(id)).Build()
            )
            .ConfigureAwait(false);
        return ((OperationResult)result).ToJson();
    }
}
