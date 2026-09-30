using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The manifest's failure window and owner, and the scheduler settings' overrides, are written
/// and read back through the Postgres mapping, and the database refuses a failure window that
/// would count no failures.
/// </summary>
public class SchedulerColumnStorageTests : TestSetup
{
    [Test]
    public async Task A_manifests_failure_window_and_owner_are_stored()
    {
        long id;
        using (var context = (IDataContext)DataContextFactory.Create())
        {
            var manifest = await SaveManifest(
                context,
                new CreateManifest
                {
                    Name = typeof(SchedulerColumnStorageTests),
                    FailureWindowSeconds = 3600,
                    Owner = "billing-service",
                }
            );
            id = manifest.Id;
        }

        using var readBack = (IDataContext)DataContextFactory.Create();
        var stored = await readBack.Manifests.AsNoTracking().SingleAsync(m => m.Id == id);

        stored.FailureWindowSeconds.Should().Be(3600);
        stored.Owner.Should().Be("billing-service");
    }

    [Test]
    public async Task The_database_refuses_a_failure_window_of_zero()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var manifest = await SaveManifest(
            context,
            new CreateManifest { Name = typeof(SchedulerColumnStorageTests) }
        );

        var act = () =>
            ((DbContext)context).Database.ExecuteSqlRawAsync(
                "UPDATE trax.manifest SET failure_window_seconds = 0 WHERE id = {0}",
                manifest.Id
            );

        (await act.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.SqlState.Should()
            .Be("23514", "a window of nothing would never dead-letter the manifest");
    }

    [Test]
    public async Task Scheduler_overrides_are_stored_as_json()
    {
        using (var context = (IDataContext)DataContextFactory.Create())
        {
            await context.SchedulerConfigs.ExecuteDeleteAsync();
            var row = new SchedulerConfig { UpdatedAt = DateTime.UtcNow };
            row.SetOverride("MaxActiveJobs", 20);
            row.SetOverride("FailureCountWindow", TimeSpan.FromHours(6));
            context.SchedulerConfigs.Add(row);
            await context.SaveChanges(CancellationToken.None);
        }

        using var readBack = (IDataContext)DataContextFactory.Create();
        var stored = await readBack.SchedulerConfigs.AsNoTracking().SingleAsync();

        stored.OverriddenSettings.Should().BeEquivalentTo("MaxActiveJobs", "FailureCountWindow");
        stored.TryGetOverride<TimeSpan>("FailureCountWindow", out var window).Should().BeTrue();
        window.Should().Be(TimeSpan.FromHours(6));

        var type = await ((DbContext)readBack)
            .Database.SqlQueryRaw<string>(
                "SELECT jsonb_typeof(overrides) AS \"Value\" FROM trax.scheduler_config"
            )
            .SingleAsync();
        type.Should().Be("object");
    }

    private static async Task<Manifest> SaveManifest(IDataContext context, CreateManifest create)
    {
        var group = new ManifestGroup
        {
            Name = $"columns-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(create);
        manifest.ManifestGroupId = group.Id;
        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);
        return manifest;
    }
}
