using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class DbPersistedOperationStorageTests
{
    private DbPersistedOperationStorage _storage = null!;
    private RecordingBroadcaster _broadcaster = null!;

    // Service provider is shared via PostgresFixture so migrations and the
    // Npgsql connection pool are amortised across the whole test process.
    // We MUST NOT dispose it from per-test TearDown.
    private IDataContextProviderFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore(
                "Postgres not reachable; integration tests skipped. Run docker compose up -d in Trax.Samples to enable."
            );

        await PostgresFixture.ClearAsync();

        var options = new PersistedOperationsBuilder()
            .UseDatabase(PostgresFixture.ConnectionString)
            .Build();

        _factory = PostgresFixture.Services.GetRequiredService<IDataContextProviderFactory>();

        _broadcaster = new RecordingBroadcaster();
        var cache = new NoOpPersistedOperationCache();

        _storage = new DbPersistedOperationStorage(
            _factory,
            options,
            cache,
            _broadcaster,
            TimeProvider.System,
            NullLogger<DbPersistedOperationStorage>.Instance
        );
    }

    [Test]
    public async Task Upsert_NewRow_Inserts()
    {
        const string doc = "query Greet { greeting }";

        var row = await _storage.UpsertAsync(
            "greet_v1",
            doc,
            options: null,
            CancellationToken.None
        );

        row.Id.Should().Be("greet_v1");
        row.OperationName.Should().Be("greet");
        row.Version.Should().Be(1);
        row.Document.Should().Be(doc);
        row.IsActive.Should().BeTrue();
        row.ShapeFingerprint.Should().HaveLength(64);
        row.TenantKey.Should().BeNull();
    }

    [Test]
    public async Task Upsert_Existing_UpdatesDocument()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );

        // Shape-preserving change: same selection set, different argument value.
        var updated = await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello(loud: true) }",
            new UpsertOptions { Description = "fix typo" },
            CancellationToken.None
        );

        updated.Document.Should().Be("query Greet { hello(loud: true) }");
        updated.Description.Should().Be("fix typo");
    }

    [Test]
    public async Task Get_Active_ReturnsRow()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );

        var fetched = await _storage.GetAsync("greet_v1", null, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be("greet_v1");
    }

    [Test]
    public async Task Get_Missing_ReturnsNull()
    {
        var fetched = await _storage.GetAsync("nonexistent_v1", null, CancellationToken.None);
        fetched.Should().BeNull();
    }

    [Test]
    public async Task Deactivate_HidesFromGet()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );
        await _storage.DeactivateAsync("greet_v1", null, "broken filter", CancellationToken.None);

        var fetched = await _storage.GetAsync("greet_v1", null, CancellationToken.None);
        fetched.Should().BeNull();
    }

    [Test]
    public async Task Restore_BringsBackActive()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );
        await _storage.DeactivateAsync("greet_v1", null, "test", CancellationToken.None);
        await _storage.RestoreAsync("greet_v1", null, CancellationToken.None);

        var fetched = await _storage.GetAsync("greet_v1", null, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.IsActive.Should().BeTrue();
        fetched.DeprecationReason.Should().BeNull();
    }

    [Test]
    public async Task Deactivate_NonexistentId_Throws()
    {
        Func<Task> act = () =>
            _storage.DeactivateAsync("nope_v1", null, "x", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Restore_NonexistentId_Throws()
    {
        Func<Task> act = () => _storage.RestoreAsync("nope_v1", null, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task List_ReturnsActiveAndDeactivated()
    {
        await _storage.UpsertAsync("a_v1", "query A { hello }", null, CancellationToken.None);
        await _storage.UpsertAsync("b_v1", "query B { world }", null, CancellationToken.None);
        await _storage.DeactivateAsync("a_v1", null, "test", CancellationToken.None);

        var list = await _storage.ListAsync(null, CancellationToken.None);
        list.Should().HaveCount(2);
        list.Single(r => r.Id == "a_v1").IsActive.Should().BeFalse();
        list.Single(r => r.Id == "b_v1").IsActive.Should().BeTrue();
    }

    [Test]
    public async Task TenantIsolation_DocumentsAreScopedByTenant()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { tenantA }",
            new UpsertOptions { TenantKey = "a" },
            CancellationToken.None
        );
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { tenantB }",
            new UpsertOptions { TenantKey = "b" },
            CancellationToken.None
        );

        var a = await _storage.GetAsync("greet_v1", "a", CancellationToken.None);
        var b = await _storage.GetAsync("greet_v1", "b", CancellationToken.None);
        var none = await _storage.GetAsync("greet_v1", null, CancellationToken.None);

        a!.Document.Should().Contain("tenantA");
        b!.Document.Should().Contain("tenantB");
        none.Should().BeNull();
    }

    [Test]
    public async Task Upsert_WritesHistoryRow()
    {
        // Shape-preserving updates so the guardrail does not reject the second.
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { v1(arg: 1) }",
            null,
            CancellationToken.None
        );
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { v1(arg: 2) }",
            null,
            CancellationToken.None
        );
        await _storage.DeactivateAsync("greet_v1", null, "test", CancellationToken.None);
        await _storage.RestoreAsync("greet_v1", null, CancellationToken.None);

        var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
        var rows = await ctx
            .PersistedOperationHistories.Where(h => h.Id == "greet_v1")
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.HistoryId)
            .Select(h => h.ChangeType)
            .ToListAsync();

        rows.Should().Equal("Upsert", "Upsert", "Deactivate", "Restore");
    }

    [Test]
    public async Task Upsert_BroadcasterThrows_OperationStillSucceeds()
    {
        // Broadcaster failure must NEVER fail the user-visible operation.
        // The DB write succeeds; staleness on other nodes self-heals via TTL.
        var throwing = new ThrowingBroadcaster();
        var options = new PersistedOperationsBuilder()
            .UseDatabase(PostgresFixture.ConnectionString)
            .Build();
        var storage = new DbPersistedOperationStorage(
            _factory,
            options,
            new NoOpPersistedOperationCache(),
            throwing,
            TimeProvider.System,
            NullLogger<DbPersistedOperationStorage>.Instance
        );

        Func<Task> act = () =>
            storage.UpsertAsync(
                "throw_v1",
                "query Q { greet(name: \"x\") }",
                null,
                CancellationToken.None
            );
        await act.Should().NotThrowAsync();

        var row = await storage.GetAsync("throw_v1", null, CancellationToken.None);
        row.Should().NotBeNull("the DB write must succeed even when the broadcaster throws");
    }

    private sealed class ThrowingBroadcaster
        : Trax.Api.GraphQL.PersistedOperations.Broadcasting.IPersistedOperationBroadcaster
    {
        public Task PublishAsync(
            Trax.Api.GraphQL.PersistedOperations.Broadcasting.PersistedOperationChangedMessage m,
            CancellationToken ct
        ) => Task.FromException(new InvalidOperationException("broadcaster blew up"));
    }

    [Test]
    public async Task Upsert_PublishesBroadcast()
    {
        await _storage.UpsertAsync("greet_v1", "query Greet { hi }", null, CancellationToken.None);

        _broadcaster
            .Messages.Should()
            .ContainSingle(m =>
                m.Id == "greet_v1" && m.ChangeType == PersistedOperationChangeType.Upsert
            );
    }

    [Test]
    public async Task Deactivate_PublishesBroadcast()
    {
        await _storage.UpsertAsync("greet_v1", "query Greet { hi }", null, CancellationToken.None);
        _broadcaster.Messages.Clear();
        await _storage.DeactivateAsync("greet_v1", null, "test", CancellationToken.None);

        _broadcaster
            .Messages.Should()
            .ContainSingle(m => m.ChangeType == PersistedOperationChangeType.Deactivate);
    }

    [Test]
    public async Task Restore_PublishesBroadcast()
    {
        await _storage.UpsertAsync("greet_v1", "query Greet { hi }", null, CancellationToken.None);
        await _storage.DeactivateAsync("greet_v1", null, "x", CancellationToken.None);
        _broadcaster.Messages.Clear();
        await _storage.RestoreAsync("greet_v1", null, CancellationToken.None);

        _broadcaster
            .Messages.Should()
            .ContainSingle(m => m.ChangeType == PersistedOperationChangeType.Restore);
    }

    [Test]
    public async Task TryReadAsync_HotPath_ReturnsDocument()
    {
        await _storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );

        var doc = await _storage.TryReadAsync(
            new OperationDocumentId("greet_v1"),
            CancellationToken.None
        );

        doc.Should().NotBeNull();
        doc!.ToString().Should().Contain("hello");
    }

    [Test]
    public async Task TryReadAsync_HotPath_Missing_ReturnsNull()
    {
        var doc = await _storage.TryReadAsync(
            new OperationDocumentId("nothing_v1"),
            CancellationToken.None
        );
        doc.Should().BeNull();
    }

    [Test]
    public async Task TryReadAsync_HotPath_Deactivated_ReturnsNull()
    {
        await _storage.UpsertAsync("greet_v1", "query Greet { hi }", null, CancellationToken.None);
        await _storage.DeactivateAsync("greet_v1", null, "test", CancellationToken.None);

        var doc = await _storage.TryReadAsync(
            new OperationDocumentId("greet_v1"),
            CancellationToken.None
        );
        doc.Should().BeNull();
    }

    [Test]
    public async Task TryReadAsync_EmptyId_ReturnsNull()
    {
        var doc = await _storage.TryReadAsync(default, CancellationToken.None);
        doc.Should().BeNull();
    }

    [Test]
    public async Task SaveAsync_HC_NotSupported_Throws()
    {
        Func<Task> act = async () =>
            await _storage.SaveAsync(
                new OperationDocumentId("x_v1"),
                new OperationDocumentSourceText("query { hi }"),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public async Task Upsert_ShapeChange_Throws_ShapeDiffViolationException()
    {
        await _storage.UpsertAsync(
            "shape_v1",
            "query Shape { greet(name: \"x\") }",
            null,
            CancellationToken.None
        );

        // Add a field — different response shape.
        Func<Task> act = () =>
            _storage.UpsertAsync(
                "shape_v1",
                "query Shape { greet(name: \"x\") echo(text: \"y\") }",
                null,
                CancellationToken.None
            );

        var ex = (await act.Should().ThrowAsync<ShapeDiffViolationException>()).Which;
        ex.Id.Should().Be("shape_v1");
        ex.OldFingerprint.Should().HaveLength(64);
        ex.NewFingerprint.Should().HaveLength(64);
        ex.OldFingerprint.Should().NotBe(ex.NewFingerprint);
    }

    [Test]
    public async Task Upsert_ShapeChange_WithBypass_Succeeds()
    {
        await _storage.UpsertAsync(
            "shape_bypass_v1",
            "query Shape { greet(name: \"x\") }",
            null,
            CancellationToken.None
        );

        var updated = await _storage.UpsertAsync(
            "shape_bypass_v1",
            "query Shape { greet(name: \"x\") echo(text: \"y\") }",
            new UpsertOptions { BypassShapeDiff = true },
            CancellationToken.None
        );

        updated.Document.Should().Contain("echo");
    }

    [Test]
    public async Task Upsert_SameShape_DifferentArgs_Succeeds_WithoutBypass()
    {
        // Argument changes do not change the response shape; the guardrail
        // should let this through.
        await _storage.UpsertAsync(
            "shape_args_v1",
            "query Q { greet(name: \"alice\") }",
            null,
            CancellationToken.None
        );

        Func<Task> act = () =>
            _storage.UpsertAsync(
                "shape_args_v1",
                "query Q { greet(name: \"bob\") }",
                null,
                CancellationToken.None
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Upsert_Concurrent_DistinctIds_BothPersistIndependently()
    {
        // Concurrent writes to distinct ids must both land. Exercises the
        // connection-pooling + transaction handling without contending on
        // the same row.
        await Task.WhenAll(
            _storage.UpsertAsync(
                "concurrent_a_v1",
                "query A { greet(name: \"a\") }",
                null,
                CancellationToken.None
            ),
            _storage.UpsertAsync(
                "concurrent_b_v1",
                "query B { greet(name: \"b\") }",
                null,
                CancellationToken.None
            )
        );

        var rowA = await _storage.GetAsync("concurrent_a_v1", null, CancellationToken.None);
        var rowB = await _storage.GetAsync("concurrent_b_v1", null, CancellationToken.None);

        rowA.Should().NotBeNull();
        rowA!.Document.Should().Contain("\"a\"");
        rowB.Should().NotBeNull();
        rowB!.Document.Should().Contain("\"b\"");
    }

    [Test]
    public async Task Upsert_Concurrent_SameId_LeavesConsistentRowAndOnlyValidExceptions()
    {
        // Two genuinely-parallel upserts to a NEW id. Two outcomes are
        // acceptable; the test pins down both:
        //
        //   A) Both succeed when one transaction's INSERT commits before the
        //      other's read snapshot resolves; the second sees the row and
        //      updates. Row reflects the LAST writer; history has 2 rows.
        //   B) One fails with DbUpdateException (PK conflict) because both
        //      tried INSERT in the same window. Row reflects the winner;
        //      history has 1 row. The failure must NOT be silent.
        //
        // The illegal outcomes — silent drop, partial commit, mismatch
        // between live row and history — are what this test rules out.
        const string id = "concurrent_same_v1";
        const string docA = "query Q { greet(name: \"A\") }";
        const string docB = "query Q { greet(name: \"B\") }";

        // Start both before awaiting either, so they actually race.
        var taskA = _storage.UpsertAsync(id, docA, null, CancellationToken.None);
        var taskB = _storage.UpsertAsync(id, docB, null, CancellationToken.None);
        var (a, b) = (await WrapAsync(taskA), await WrapAsync(taskB));

        var successes = new[] { a, b }.Where(r => r.Success).ToList();
        var failures = new[] { a, b }.Where(r => !r.Success).ToList();

        successes.Should().NotBeEmpty("at least one upsert must commit");
        foreach (var failure in failures)
            failure
                .Exception!.Should()
                .BeAssignableTo<DbUpdateException>(
                    "any concurrent failure must surface as DbUpdateException, never a silent drop"
                );

        var row = await _storage.GetAsync(id, null, CancellationToken.None);
        row.Should().NotBeNull();
        row!.Document.Should().BeOneOf(successes.Select(s => s.Document!).ToArray());

        var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
        var historyDocs = await ctx
            .PersistedOperationHistories.Where(h =>
                h.Id == id && h.ChangeType == PersistedOperationChangeType.Upsert
            )
            .Select(h => h.Document)
            .ToListAsync();

        historyDocs
            .Should()
            .HaveCount(
                successes.Count,
                "history row count must match the number of successful upserts (1 or 2)"
            );
        historyDocs.Should().BeEquivalentTo(successes.Select(s => s.Document));
    }

    private static async Task<WrapResult> WrapAsync(Task<PersistedOperation> t)
    {
        try
        {
            var row = await t;
            return new WrapResult(true, row.Document, null);
        }
        catch (Exception ex)
        {
            return new WrapResult(false, null, ex);
        }
    }

    private readonly record struct WrapResult(bool Success, string? Document, Exception? Exception);

    [Test]
    public async Task Upsert_InvalidIdFormat_Throws()
    {
        Func<Task> act = () =>
            _storage.UpsertAsync("not-versioned", "query { x }", null, CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();
    }

    [Test]
    public async Task Upsert_EmptyDocument_Throws()
    {
        Func<Task> act = () =>
            _storage.UpsertAsync("greet_v1", string.Empty, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task TryReadAsync_UsesCache_WhenConfigured()
    {
        var memCache = new InMemoryPersistedOperationCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()
            ),
            new PersistedOperationsBuilder()
                .UseDatabase(PostgresFixture.ConnectionString)
                .WithInMemoryCache()
                .Build()
        );
        var storage = new DbPersistedOperationStorage(
            _factory,
            new PersistedOperationsBuilder().UseDatabase(PostgresFixture.ConnectionString).Build(),
            memCache,
            new NoOpPersistedOperationBroadcaster(),
            TimeProvider.System,
            NullLogger<DbPersistedOperationStorage>.Instance
        );

        await storage.UpsertAsync(
            "greet_v1",
            "query Greet { hello }",
            null,
            CancellationToken.None
        );

        // First read: populates cache from DB.
        var first = await storage.TryReadAsync(
            new OperationDocumentId("greet_v1"),
            CancellationToken.None
        );

        // Manually wipe DB to verify subsequent read is served from cache.
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            await ctx.PersistedOperations.ExecuteDeleteAsync();
        }

        var second = await storage.TryReadAsync(
            new OperationDocumentId("greet_v1"),
            CancellationToken.None
        );

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.ToString().Should().Contain("hello");
    }

    private sealed class RecordingBroadcaster : IPersistedOperationBroadcaster
    {
        public List<PersistedOperationChangedMessage> Messages { get; } = new();

        public Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
