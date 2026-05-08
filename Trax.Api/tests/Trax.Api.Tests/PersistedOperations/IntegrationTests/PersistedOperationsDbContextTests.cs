using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;
using Trax.Effect.Models.PersistedOperationHistory;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// EF round-trip tests against a real Postgres instance using the existing
/// Trax <c>IDataContext</c>. Ensures every column on
/// <see cref="PersistedOperation"/> and <see cref="PersistedOperationHistory"/>
/// persists and reads back exactly, including the default values, the
/// composite primary key, and the JSON/text columns. Mirrors the
/// SchedulerConfig_RoundTrip_PersistsAllColumns pattern used elsewhere in
/// Trax.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistedOperationsDbContextTests
{
    private IDataContextProviderFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable.");

        await PostgresFixture.ClearAsync();

        // Shared provider via PostgresFixture: one migrate per process, one
        // connection pool. Do NOT dispose from this fixture.
        _factory = PostgresFixture.Services.GetRequiredService<IDataContextProviderFactory>();
    }

    [Test]
    public async Task PersistedOperation_RoundTrip_PersistsAllColumns()
    {
        var now = DateTime.UtcNow;
        var row = new PersistedOperation
        {
            TenantKey = "tenant-x",
            Id = "userProfile_v3",
            OperationName = "UserProfile",
            Version = 3,
            Document = "query UserProfile($id: Int!) { user(id: $id) { id name email } }",
            ShapeFingerprint = new string('a', 64),
            IsActive = false,
            DeprecationReason = "broken filter",
            Description = "manifest entry for v3",
            CreatedAt = now,
            UpdatedAt = now,
        };

        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperations.Add(row);
            await ctx.SaveChanges(CancellationToken.None);
        }

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var found = await read
            .PersistedOperations.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantKey == "tenant-x" && p.Id == "userProfile_v3");

        found.Should().NotBeNull();
        found!.OperationName.Should().Be("UserProfile");
        found.Version.Should().Be(3);
        found.Document.Should().Contain("$id: Int!");
        found.ShapeFingerprint.Should().Be(new string('a', 64));
        found.IsActive.Should().BeFalse();
        found.DeprecationReason.Should().Be("broken filter");
        found.Description.Should().Be("manifest entry for v3");
        found.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        found.UpdatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task PersistedOperation_CompositeKey_AllowsSameIdAcrossTenants()
    {
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperations.Add(
                new PersistedOperation
                {
                    TenantKey = "a",
                    Id = "shared_v1",
                    OperationName = "Shared",
                    Version = 1,
                    Document = "query Shared { a }",
                    ShapeFingerprint = "h1",
                }
            );
            ctx.PersistedOperations.Add(
                new PersistedOperation
                {
                    TenantKey = "b",
                    Id = "shared_v1",
                    OperationName = "Shared",
                    Version = 1,
                    Document = "query Shared { b }",
                    ShapeFingerprint = "h2",
                }
            );
            await ctx.SaveChanges(CancellationToken.None);
        }

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var rows = await read
            .PersistedOperations.AsNoTracking()
            .Where(p => p.Id == "shared_v1")
            .OrderBy(p => p.TenantKey)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Document.Should().Contain("a");
        rows[1].Document.Should().Contain("b");
    }

    [Test]
    public async Task PersistedOperation_Defaults_IsActiveTrueAndTimestampsPopulated()
    {
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperations.Add(
                new PersistedOperation
                {
                    TenantKey = "",
                    Id = "defaults_v1",
                    OperationName = "Defaults",
                    Version = 1,
                    Document = "query Defaults { x }",
                    ShapeFingerprint = "fp",
                }
            );
            await ctx.SaveChanges(CancellationToken.None);
        }

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var row = await read
            .PersistedOperations.AsNoTracking()
            .FirstAsync(p => p.Id == "defaults_v1");

        row.IsActive.Should().BeTrue();
        row.CreatedAt.Should().NotBe(default);
        row.UpdatedAt.Should().NotBe(default);
    }

    [Test]
    public async Task PersistedOperation_PrimaryKeyConflict_Throws()
    {
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperations.Add(
                new PersistedOperation
                {
                    TenantKey = "",
                    Id = "dup_v1",
                    OperationName = "Dup",
                    Version = 1,
                    Document = "{ x }",
                    ShapeFingerprint = "fp",
                }
            );
            await ctx.SaveChanges(CancellationToken.None);
        }

        var ctx2 = await _factory.CreateDbContextAsync(CancellationToken.None);
        ctx2.PersistedOperations.Add(
            new PersistedOperation
            {
                TenantKey = "",
                Id = "dup_v1",
                OperationName = "Dup",
                Version = 2,
                Document = "{ y }",
                ShapeFingerprint = "fp",
            }
        );

        Func<Task> act = () => ctx2.SaveChanges(CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task PersistedOperationHistory_RoundTrip_PersistsAllColumns()
    {
        var now = DateTime.UtcNow;
        var entry = new PersistedOperationHistory
        {
            TenantKey = "",
            Id = "history_v1",
            Document = "query H { z }",
            ShapeFingerprint = new string('b', 64),
            ChangeType = "Upsert",
            ChangedAt = now,
            ChangedReason = "initial",
        };

        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperationHistories.Add(entry);
            await ctx.SaveChanges(CancellationToken.None);
        }

        // History row should have a DB-generated history_id.
        entry.HistoryId.Should().BeGreaterThan(0);

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var found = await read
            .PersistedOperationHistories.AsNoTracking()
            .FirstAsync(h => h.Id == "history_v1");

        found.Document.Should().Contain("z");
        found.ShapeFingerprint.Should().Be(new string('b', 64));
        found.ChangeType.Should().Be("Upsert");
        found.ChangedReason.Should().Be("initial");
        found.ChangedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task PersistedOperationHistory_OrderedByChangedAtDesc_ReturnsMostRecentFirst()
    {
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            for (var i = 0; i < 3; i++)
            {
                ctx.PersistedOperationHistories.Add(
                    new PersistedOperationHistory
                    {
                        TenantKey = "",
                        Id = "ordered_v1",
                        Document = $"v{i}",
                        ShapeFingerprint = "fp",
                        ChangeType = "Upsert",
                        ChangedAt = DateTime.UtcNow.AddMinutes(i),
                        ChangedReason = $"step{i}",
                    }
                );
            }
            await ctx.SaveChanges(CancellationToken.None);
        }

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var rows = await read
            .PersistedOperationHistories.AsNoTracking()
            .Where(h => h.Id == "ordered_v1")
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => h.Document)
            .ToListAsync();

        rows.Should().Equal("v2", "v1", "v0");
    }

    [Test]
    public async Task PersistedOperation_NullableColumns_PersistAsNull()
    {
        {
            var ctx = await _factory.CreateDbContextAsync(CancellationToken.None);
            ctx.PersistedOperations.Add(
                new PersistedOperation
                {
                    TenantKey = "",
                    Id = "nulls_v1",
                    OperationName = "Nulls",
                    Version = 1,
                    Document = "{ x }",
                    ShapeFingerprint = "fp",
                    DeprecationReason = null,
                    Description = null,
                }
            );
            await ctx.SaveChanges(CancellationToken.None);
        }

        var read = await _factory.CreateDbContextAsync(CancellationToken.None);
        var row = await read.PersistedOperations.AsNoTracking().FirstAsync(p => p.Id == "nulls_v1");
        row.DeprecationReason.Should().BeNull();
        row.Description.Should().BeNull();
    }
}
