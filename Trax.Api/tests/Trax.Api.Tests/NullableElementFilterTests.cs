using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Every operation a nullable-element scalar array's list filter offers runs on Postgres. These
/// run the query end to end over Npgsql, because an operation EF Core cannot translate passes
/// validation and fails only when the query executes.
/// </summary>
[TestFixture]
public class NullableElementFilterTests
{
    private const string Database = "trax_api_posture_nullable_elements";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);

        var options = new DbContextOptionsBuilder<NullableElementContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var context = new NullableElementContext(options))
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            context.Rows.AddRange(
                new NullableElementRow { Id = 1, Scores = [1, 1] },
                new NullableElementRow { Id = 2, Scores = [1, 2] },
                new NullableElementRow { Id = 3, Scores = [null, 3] }
            );
            await context.SaveChangesAsync();
        }

        _host = await PostureHost.StartAsync(
            g => g.AddDbContext<NullableElementContext>(),
            s => s.AddDbContextFactory<NullableElementContext>(o => o.UseNpgsql(connectionString))
        );
    }

    [OneTimeTearDown]
    public void StopAsync() => _host.Dispose();

    private Task<string> FilterAsync(string where) =>
        _host.PostAsync(
            $"{{ discover {{ nullableElementRows(where: {{ scores: {where} }}, order: [{{ id: ASC }}]) {{ nodes {{ id }} }} }} }}"
        );

    [TestCase("{ some: { eq: 3 } }", new[] { 3 })]
    [TestCase("{ some: { eq: null } }", new[] { 3 })]
    [TestCase("{ some: { in: [2, 3] } }", new[] { 2, 3 })]
    [TestCase("{ some: { gt: 1 } }", new[] { 2, 3 })]
    [TestCase("{ none: { eq: 2 } }", new[] { 1, 3 })]
    [TestCase("{ all: { eq: 1 } }", new[] { 1 })]
    public async Task AnOperationThatTranslates_ReturnsTheRowsPostgresSelects(
        string where,
        int[] ids
    )
    {
        var json = await FilterAsync(where);

        json.Should().NotContain("\"errors\"");
        foreach (var id in ids)
            json.Should().Contain($"{{\"id\":{id}}}");
        json.Split("\"id\"").Length.Should().Be(ids.Length + 1);
    }

    [TestCase("{ some: { neq: 1 } }")]
    [TestCase("{ all: { neq: 1 } }")]
    [TestCase("{ none: { neq: 1 } }")]
    public async Task Neq_IsNotOffered(string where)
    {
        var json = await FilterAsync(where);

        json.Should().Contain("\"errors\"").And.Contain("neq").And.NotContain("\"nodes\"");
    }

    public sealed class NullableElementContext(DbContextOptions<NullableElementContext> options)
        : DbContext(options)
    {
        public DbSet<NullableElementRow> Rows => Set<NullableElementRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasDefaultSchema("nullable_elements");
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public sealed class NullableElementRow
    {
        public int Id { get; set; }

        public int?[] Scores { get; set; } = [];
    }
}
