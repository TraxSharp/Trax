using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Api.Tests;

[TestFixture]
public class TraxHealthServiceTests
{
    // Pool tuning matches the AuthE2E hardening from PR #41 after the same
    // class of CI flake here: aggressive Connection Pruning Interval=1 +
    // Idle Lifetime=1 forced every SetUp to pay TCP+auth and timed out under
    // contention. Pool Size=8 across the four test fixtures in this assembly
    // stays well under Postgres's default max_connections=100.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_api_health;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;
    private TraxHealthService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        _service = new TraxHealthService(_factory);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (DbContext)db;
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.metadata, trax.work_queue, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    [Test]
    public async Task GetHealthAsync_NoActivity_ReportsHealthyAndZeroes()
    {
        var status = await _service.GetHealthAsync();

        status.Status.Should().Be("Healthy");
        status.Description.Should().Be("All systems operational");
        status.QueueDepth.Should().Be(0);
        status.InProgress.Should().Be(0);
        status.FailedLastHour.Should().Be(0);
        status.DeadLetters.Should().Be(0);
    }

    [Test]
    public async Task GetHealthAsync_InProgressMetadata_CountedInInProgress()
    {
        await SeedMetadata(state: TrainState.InProgress, endTime: null);
        await SeedMetadata(state: TrainState.InProgress, endTime: null);
        await SeedMetadata(state: TrainState.Completed, endTime: DateTime.UtcNow);

        var status = await _service.GetHealthAsync();

        status.InProgress.Should().Be(2);
    }

    [Test]
    public async Task GetHealthAsync_RecentFailures_CountedInFailedLastHour()
    {
        await SeedMetadata(TrainState.Failed, DateTime.UtcNow.AddMinutes(-30));
        await SeedMetadata(TrainState.Failed, DateTime.UtcNow.AddHours(-2));

        var status = await _service.GetHealthAsync();

        status.FailedLastHour.Should().Be(1);
    }

    [Test]
    public async Task GetHealthAsync_ManyFailures_ReportsDegraded()
    {
        for (var i = 0; i < 11; i++)
            await SeedMetadata(TrainState.Failed, DateTime.UtcNow.AddMinutes(-10));

        var status = await _service.GetHealthAsync();

        status.Status.Should().Be("Degraded");
        status.Description.Should().Contain("Elevated");
    }

    private async Task SeedMetadata(TrainState state, DateTime? endTime)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.SomeTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        if (endTime.HasValue)
            meta.EndTime = endTime;
        await db.Track(meta);
        await db.SaveChanges(default);
    }
}
