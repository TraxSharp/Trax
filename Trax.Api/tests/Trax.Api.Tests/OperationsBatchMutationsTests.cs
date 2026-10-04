using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// The cancel and enable/disable mutations the dashboard offers on a selection call the same
/// <see cref="IOperationsService"/> methods the dashboard does, so a batch from the API flags,
/// skips and refuses exactly what the same batch from the dashboard would. Runs against
/// Postgres, where the service writes each batch as one statement. A refused batch is a failed
/// payload, per <c>docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md")]
public class OperationsBatchMutationsTests
{
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;
    private ICancellationRegistry _registry = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
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
        _registry = Substitute.For<ICancellationRegistry>();
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.work_queue, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    /// <summary>The service as a host resolves it: with a provider holding this host's registry.</summary>
    private IOperationsService Operations
    {
        get
        {
            var scope = new ServiceCollection().AddSingleton(_registry).BuildServiceProvider();
            return new OperationsService(
                Substitute.For<ITrainDiscoveryService>(),
                _factory,
                new SchedulerConfiguration(),
                Substitute.For<ITrainExecutionService>(),
                scope
            );
        }
    }

    private async Task<long> SeedRun(TrainState state)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Batch",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        await db.Track(meta);
        await db.SaveChanges(default);
        return meta.Id;
    }

    private async Task<long> SeedGroup(bool enabled)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var group = new ManifestGroup
        {
            Name = $"group-{Guid.NewGuid():N}",
            IsEnabled = enabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(group);
        await db.SaveChanges(default);
        return group.Id;
    }

    private async Task<long> SeedManifest(long groupId, bool enabled)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(BatchFakeTrain) });
        manifest.ManifestGroupId = groupId;
        manifest.IsEnabled = enabled;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return manifest.Id;
    }

    [Test]
    public async Task CancelExecution_ARunOnThisHost_IsAlsoCancelledAtOnce()
    {
        var id = await SeedRun(TrainState.InProgress);

        var response = await new OperationsMutations().CancelExecution(id, Operations, default);

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(1);
        _registry.Received(1).TryCancel(id);
    }

    [Test]
    public async Task CancelExecution_ATerminalRun_IsRefusedAndNotSignalled()
    {
        var id = await SeedRun(TrainState.Completed);

        var response = await new OperationsMutations().CancelExecution(id, Operations, default);

        response.Success.Should().BeFalse();
        response.Count.Should().Be(0);
        response.Message.Should().Contain("not cancellable");
        _registry.DidNotReceiveWithAnyArgs().TryCancel(default);
    }

    [Test]
    public async Task CancelExecutions_FlagsPendingAndRunning_AndSkipsTheRest()
    {
        var pending = await SeedRun(TrainState.Pending);
        var running = await SeedRun(TrainState.InProgress);
        var completed = await SeedRun(TrainState.Completed);

        var response = await new OperationsMutations().CancelExecutions(
            [pending, running, completed, 999_999],
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(2);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.Where(m => m.CancellationRequested).Select(m => m.Id).ToListAsync())
            .Should()
            .BeEquivalentTo([pending, running]);
    }

    [Test]
    public async Task CancelExecutions_AnEmptyList_IsRefused()
    {
        var response = await new OperationsMutations().CancelExecutions([], Operations, default);

        response
            .Success.Should()
            .BeFalse(
                "an empty batch is a refusal, returned in the payload (docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md)"
            );
        response.Message.Should().Be("No ids were given.");
    }

    [Test]
    public async Task CancelExecutions_MoreThanOneBatch_IsRefusedAndFlagsNothing()
    {
        var id = await SeedRun(TrainState.Pending);
        var ids = Enumerable
            .Range(0, OperationsService.MaxBatchSize)
            .Select(i => (long)i + 1_000_000)
            .Append(id)
            .ToArray();

        var response = await new OperationsMutations().CancelExecutions(ids, Operations, default);

        response.Success.Should().BeFalse();
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.AnyAsync(m => m.CancellationRequested)).Should().BeFalse();
    }

    [Test]
    public async Task SetManifestsEnabled_WritesOnlyTheManifestsWhoseFlagDiffers()
    {
        var group = await SeedGroup(enabled: true);
        var on = await SeedManifest(group, enabled: true);
        var off = await SeedManifest(group, enabled: false);

        var response = await new OperationsMutations().SetManifestsEnabled(
            [on, off],
            false,
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(1);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Manifests.AnyAsync(m => m.IsEnabled)).Should().BeFalse();
    }

    [Test]
    public async Task SetManifestsEnabled_AnEmptyList_IsRefusedRatherThanMeaningAll()
    {
        var group = await SeedGroup(enabled: true);
        await SeedManifest(group, enabled: true);

        var response = await new OperationsMutations().SetManifestsEnabled(
            [],
            false,
            Operations,
            default
        );

        response
            .Success.Should()
            .BeFalse(
                "an empty batch is a refusal, returned in the payload (docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md)"
            );
        response.Message.Should().Be("No ids were given.");
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Manifests.AllAsync(m => m.IsEnabled)).Should().BeTrue();
    }

    [Test]
    public async Task SetManifestsEnabled_MoreThanOneBatch_IsRefusedAndWritesNothing()
    {
        var group = await SeedGroup(enabled: true);
        var id = await SeedManifest(group, enabled: true);
        var ids = Enumerable
            .Range(0, OperationsService.MaxBatchSize)
            .Select(i => (long)i + 1_000_000)
            .Append(id)
            .ToArray();

        var response = await new OperationsMutations().SetManifestsEnabled(
            ids,
            false,
            Operations,
            default
        );

        response.Success.Should().BeFalse();
        response.Count.Should().Be(0);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Manifests.SingleAsync(m => m.Id == id)).IsEnabled.Should().BeTrue();
    }

    [Test]
    public async Task SetManifestGroupsEnabled_WritesOnlyTheListedGroups()
    {
        var listed = await SeedGroup(enabled: true);
        var other = await SeedGroup(enabled: true);

        var response = await new ManifestGroupMutations().SetManifestGroupsEnabled(
            [listed],
            false,
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(1);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.ManifestGroups.SingleAsync(g => g.Id == listed)).IsEnabled.Should().BeFalse();
        (await db.ManifestGroups.SingleAsync(g => g.Id == other)).IsEnabled.Should().BeTrue();
    }

    [Test]
    public async Task SetManifestGroupsEnabled_AnEmptyList_IsRefusedRatherThanMeaningAll()
    {
        await SeedGroup(enabled: true);

        var response = await new ManifestGroupMutations().SetManifestGroupsEnabled(
            [],
            false,
            Operations,
            default
        );

        response.Success.Should().BeFalse();
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.ManifestGroups.AllAsync(g => g.IsEnabled)).Should().BeTrue();
    }

    [Test]
    public async Task SetAllManifestGroupsEnabled_WritesEveryGroupWhoseFlagDiffers()
    {
        await SeedGroup(enabled: true);
        await SeedGroup(enabled: true);
        await SeedGroup(enabled: false);

        var response = await new ManifestGroupMutations().SetAllManifestGroupsEnabled(
            false,
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(2);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.ManifestGroups.AnyAsync(g => g.IsEnabled)).Should().BeFalse();
    }

    private class BatchFakeTrain { }
}
