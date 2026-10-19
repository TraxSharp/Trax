using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Services.BulkOperations;
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
/// A bulk operation over a filter runs its batches through the same methods, and refuses a
/// snapshot that is not the count the operator confirmed, per
/// <c>docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md")]
[Property("adr", "docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md")]
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

    [Test]
    public async Task TriggerManifests_QueuesEachManifest_AndNotesTheUnknownIds()
    {
        var groupId = await SeedGroup(enabled: true);
        var first = await SeedManifest(groupId, enabled: true);
        var second = await SeedManifest(groupId, enabled: true);

        var response = await new OperationsMutations().TriggerManifests(
            [first, second, 999_999],
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Matched.Should().Be(2);
        response.Queued.Should().Be(2);
        response.AlreadyQueued.Should().Be(0);
        response.Skipped.Should().Be(1);
        response.Notes.Should().ContainSingle().Which.Id.Should().Be(999_999);
        await using var db = await _factory.CreateDbContextAsync(default);
        (
            await db
                .WorkQueues.Where(q => q.Status == WorkQueueStatus.Queued)
                .Select(q => q.ManifestId)
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(new long?[] { first, second });
    }

    [Test]
    public async Task TriggerManifests_AManifestAlreadyQueued_IsNotQueuedTwice()
    {
        var groupId = await SeedGroup(enabled: true);
        var manifest = await SeedManifest(groupId, enabled: true);
        await new OperationsMutations().TriggerManifests([manifest], Operations, default);

        var response = await new OperationsMutations().TriggerManifests(
            [manifest],
            Operations,
            default,
            askAfresh: true
        );

        response.Success.Should().BeTrue(response.Message);
        response.Queued.Should().Be(0);
        response.AlreadyQueued.Should().Be(1);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.WorkQueues.CountAsync(q => q.ManifestId == manifest)).Should().Be(1);
    }

    [Test]
    public async Task TriggerManifests_AnEmptyList_IsRefused()
    {
        var response = await new OperationsMutations().TriggerManifests([], Operations, default);

        response.Success.Should().BeFalse();
        response.Matched.Should().Be(0);
        response.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task TriggerManifests_MoreThanOneBatch_IsRefusedAndQueuesNothing()
    {
        var groupId = await SeedGroup(enabled: true);
        var manifest = await SeedManifest(groupId, enabled: true);
        var ids = Enumerable
            .Range(1, OperationsService.MaxBatchSize)
            .Select(i => manifest + i)
            .Prepend(manifest)
            .ToArray();

        var response = await new OperationsMutations().TriggerManifests(ids, Operations, default);

        response.Success.Should().BeFalse();
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.WorkQueues.AnyAsync()).Should().BeFalse();
    }

    [Test]
    public async Task TriggerGroups_TriggersEveryMemberOfEachGroup_AndNotesTheUnknownIds()
    {
        var groupA = await SeedGroup(enabled: true);
        var groupB = await SeedGroup(enabled: true);
        await SeedManifest(groupA, enabled: true);
        await SeedManifest(groupA, enabled: true);
        await SeedManifest(groupB, enabled: true);

        var response = await new OperationsMutations().TriggerGroups(
            [groupA, groupB, 999_999],
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Matched.Should().Be(2, "matched counts the groups found");
        response.Queued.Should().Be(3, "the other counts are of manifests");
        response.Skipped.Should().Be(1);
        response.Notes.Should().ContainSingle().Which.Id.Should().Be(999_999);
    }

    [Test]
    public async Task TriggerGroups_AnEmptyList_IsRefused()
    {
        (await new OperationsMutations().TriggerGroups([], Operations, default))
            .Success.Should()
            .BeFalse();
    }

    [Test]
    public async Task CancelGroups_FlagsTheRunningRunsOfEveryListedGroup()
    {
        var groupA = await SeedGroup(enabled: true);
        var groupB = await SeedGroup(enabled: true);
        var other = await SeedGroup(enabled: true);
        var inA = await SeedRunOf(await SeedManifest(groupA, enabled: true), TrainState.Pending);
        var inB = await SeedRunOf(await SeedManifest(groupB, enabled: true), TrainState.InProgress);
        var done = await SeedRunOf(await SeedManifest(groupB, enabled: true), TrainState.Completed);
        var outside = await SeedRunOf(
            await SeedManifest(other, enabled: true),
            TrainState.InProgress
        );

        var response = await new OperationsMutations().CancelGroups(
            [groupA, groupB],
            Operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
        response.Count.Should().Be(2);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.Where(m => m.CancellationRequested).Select(m => m.Id).ToListAsync())
            .Should()
            .BeEquivalentTo([inA, inB]);
        (
            await db.Metadatas.AnyAsync(m =>
                (m.Id == done || m.Id == outside) && m.CancellationRequested
            )
        )
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task CancelGroups_MoreThanOneBatch_IsRefused()
    {
        var ids = Enumerable.Range(1, OperationsService.MaxBatchSize + 1).Select(i => (long)i);

        var response = await new OperationsMutations().CancelGroups(
            ids.ToArray(),
            Operations,
            default
        );

        response.Success.Should().BeFalse();
        response.Count.Should().Be(0);
    }

    private const string BulkAdr =
        "docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md";

    private const string BulkTrain = "Trax.X.BulkTarget";

    /// <summary>
    /// A container holding what a bulk operation's batches resolve, with the API's audit
    /// observer writing into <paramref name="channel"/>.
    /// </summary>
    private ServiceProvider BulkHost(out TraxAuditChannel channel)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_factory);
        services.AddScoped(_ => Operations);
        services.AddSingleton(Options.Create(new TraxAuditOptions()));
        services.AddSingleton<TraxAuditChannel>();
        services.AddSingleton<IBulkOperationObserver, TraxBulkOperationAudit>();
        services.AddSingleton<BulkOperationJobs>();
        var provider = services.BuildServiceProvider();
        channel = provider.GetRequiredService<TraxAuditChannel>();
        return provider;
    }

    private async Task<long[]> SeedRuns(int count, TrainState state)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var runs = new List<Metadata>();
        for (var i = 0; i < count; i++)
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = BulkTrain,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            meta.TrainState = state;
            await db.Track(meta);
            runs.Add(meta);
        }
        await db.SaveChanges(default);
        return runs.Select(r => r.Id).ToArray();
    }

    private static ClaimsPrincipal Operator() =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(TraxAuthClaimTypes.PrincipalId, "apikey:ops-1"),
                    new Claim(ClaimTypes.Name, "Ops One"),
                ],
                "ApiKey"
            )
        );

    private static BulkOperationFilter BulkRuns() =>
        new() { Executions = new BulkExecutionFilter { TrainName = BulkTrain } };

    [Test]
    public async Task StartBulkOperation_ACancelOver250Runs_ThreeOfWhichFinishFirst_FailsThoseThreeWithAReason()
    {
        var ids = await SeedRuns(250, TrainState.InProgress);
        var finishedFirst = ids.Take(3).ToArray();
        await using var host = BulkHost(out var channel);
        var jobs = host.GetRequiredService<BulkOperationJobs>();
        // The three end between the snapshot and their batch, as a run finishing on its own does.
        jobs.BeforeFirstBatch = async ct =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await ((DbContext)db)
                .Set<Metadata>()
                .Where(m => finishedFirst.Contains(m.Id))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.TrainState, TrainState.Completed),
                    ct
                );
        };

        var started = await new OperationsMutations().StartBulkOperation(
            BulkOperationKind.Cancel,
            BulkRuns(),
            expectedCount: 250,
            new TraxApplicationServices(host),
            Operator(),
            default
        );
        started.Success.Should().BeTrue(started.Message);
        started.Operation!.Status.Should().Be(BulkOperationStatus.Running);
        await jobs.Current;

        var operation = jobs.Get(started.Operation.Id)!;
        operation.Status.Should().Be(BulkOperationStatus.Finished, operation.Message);
        operation.Total.Should().Be(250);
        operation.Done.Should().Be(247);
        operation.Failed.Should().Be(3);
        operation.Actor.Should().Be("apikey:ops-1", $"the caller is recorded ({BulkAdr})");
        operation
            .GetFailures()
            .Should()
            .BeEquivalentTo(
                finishedFirst.Select(id => new BulkOperationFailure(
                    id,
                    "It had already ended (completed)."
                ))
            );
        operation
            .FailureReasons.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new BulkOperationFailureReason("It had already ended (completed).", 3));

        await using (var db = await _factory.CreateDbContextAsync(default))
            (await db.Metadatas.CountAsync(m => m.CancellationRequested)).Should().Be(247);

        channel
            .Reader.TryRead(out var entry)
            .Should()
            .BeTrue($"the outcome is audited ({BulkAdr})");
        entry!.PrincipalId.Should().Be("apikey:ops-1");
        entry.OperationName.Should().Be("bulkOperation");
        entry.Success.Should().BeTrue();
        entry.Metadata!["done"].Should().Be("247");
        entry.Metadata["failed"].Should().Be("3");
        entry.Document.Should().BeEmpty("the filter is a value the caller sent");
    }

    [Test]
    public async Task StartBulkOperation_ACountOtherThanTheConfirmedOne_IsRefusedAndChangesNothing()
    {
        await SeedRuns(5, TrainState.Pending);
        await using var host = BulkHost(out _);

        var response = await new OperationsMutations().StartBulkOperation(
            BulkOperationKind.Cancel,
            BulkRuns(),
            expectedCount: 4,
            new TraxApplicationServices(host),
            Operator(),
            default
        );

        response
            .Success.Should()
            .BeFalse($"the snapshot must match the confirmed count ({BulkAdr})");
        response.Matched.Should().Be(5);
        response.Operation.Should().BeNull();
        response.Message.Should().Contain("matches 5 run(s) now, not the 4 confirmed");
        host.GetRequiredService<BulkOperationJobs>().List().Should().BeEmpty();
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.AnyAsync(m => m.CancellationRequested)).Should().BeFalse();
    }

    [Test]
    public async Task StartBulkOperation_AFilterForAnotherList_IsRefused()
    {
        await using var host = BulkHost(out _);

        var response = await new OperationsMutations().StartBulkOperation(
            BulkOperationKind.Disable,
            BulkRuns(),
            expectedCount: 0,
            new TraxApplicationServices(host),
            Operator(),
            default
        );

        response.Success.Should().BeFalse();
        response.Message.Should().Be("DISABLE applies to manifests: give filter.manifests.");
    }

    [Test]
    public async Task StartBulkOperation_DisableEveryManifestOfAGroup_DisablesThemAll()
    {
        var group = await SeedGroup(enabled: true);
        await SeedManifest(group, enabled: true);
        await SeedManifest(group, enabled: false);
        var outside = await SeedManifest(await SeedGroup(enabled: true), enabled: true);
        await using var host = BulkHost(out _);
        var jobs = host.GetRequiredService<BulkOperationJobs>();

        var started = await new OperationsMutations().StartBulkOperation(
            BulkOperationKind.Disable,
            new BulkOperationFilter
            {
                Manifests = new BulkManifestFilter { ManifestGroupId = group },
            },
            expectedCount: 2,
            new TraxApplicationServices(host),
            null,
            default
        );
        started.Success.Should().BeTrue(started.Message);
        await jobs.Current;

        var operation = jobs.Get(started.Operation!.Id)!;
        operation.Done.Should().Be(2, "a manifest already disabled is in the state asked for");
        operation.Failed.Should().Be(0);
        operation.Actor.Should().Be("<anonymous>");
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Manifests.Where(m => m.IsEnabled).Select(m => m.Id).ToListAsync())
            .Should()
            .BeEquivalentTo([outside]);
    }

    [Test]
    public void RequeueBatches_KeepEachManifestsDeadLettersInOneBatch()
    {
        // 300 of manifest 1, 300 of manifest 2, 700 of manifest 3, 1200 of manifest 4, 10 of 5.
        var manifests = Enumerable
            .Repeat(1L, 300)
            .Concat(Enumerable.Repeat(2L, 300))
            .Concat(Enumerable.Repeat(3L, 700))
            .Concat(Enumerable.Repeat(4L, 1200))
            .Concat(Enumerable.Repeat(5L, 10))
            .ToList();

        BulkOperationTargets
            .RequeueBatches(manifests)
            .Should()
            .Equal(
                [300, 600, 1300, 2300, 2500, 2510],
                $"a manifest split across batches would leave its second half awaiting ({BulkAdr})"
            );
    }

    private async Task<long> SeedRunOf(long manifestId, TrainState state)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Batch",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifestId,
            }
        );
        meta.TrainState = state;
        await db.Track(meta);
        await db.SaveChanges(default);
        return meta.Id;
    }

    private class BatchFakeTrain { }
}
