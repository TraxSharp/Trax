using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;
using Trax.Scheduler.Trains.MetadataCleanup.Junctions;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A whitelisted train can be kept for longer, or shorter, than the configured default.
///
/// <para>These build their own <see cref="MetadataCleanupConfiguration"/> and junction rather
/// than mutating the fixture's singleton, because a whitelist entry cannot be removed once added
/// and would leak into every later test in the class.</para>
///
/// <para>Enforces <c>docs/adr/0003-a-runtime-retention-override-replaces-only-the-default.md</c>:
/// the runtime override moves the default group's cutoff and leaves a retention set in code
/// alone.</para>
/// </summary>
[Property("adr", "docs/adr/0003-a-runtime-retention-override-replaces-only-the-default.md")]
[TestFixture]
[NonParallelizable]
public class MetadataRetentionTests : TestSetup
{
    private const string FastTrain = "Trax.Tests.IDeltaImportAllTrain";
    private const string SlowTrain = "Trax.Tests.IPatchCustomerTrain";

    private DeleteExpiredMetadataJunction BuildJunction(
        MetadataCleanupConfiguration cleanup,
        ITrainDiscoveryService? discovery = null
    ) =>
        new(
            DataContext,
            new SchedulerConfiguration { MetadataCleanup = cleanup },
            NullLogger<DeleteExpiredMetadataJunction>.Instance,
            discovery
        );

    private static MetadataCleanupConfiguration NewCleanup(TimeSpan? defaultRetention = null) =>
        new() { RetentionPeriod = defaultRetention ?? TimeSpan.FromMinutes(30) };

    #region Per-train cutoffs

    [Test]
    public async Task Run_SweepsEachTrainAtItsOwnRetention()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(FastTrain);
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        var fastRecent = await Seed(FastTrain, DateTime.UtcNow.AddHours(-1));
        var fastAncient = await Seed(FastTrain, DateTime.UtcNow.AddDays(-31));
        var slowRecent = await Seed(SlowTrain, DateTime.UtcNow.AddHours(-1));
        var slowAncient = await Seed(SlowTrain, DateTime.UtcNow.AddDays(-31));

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        (await Exists(fastRecent)).Should().BeFalse("1 hour is past the 30 minute default");
        (await Exists(fastAncient)).Should().BeFalse();
        (await Exists(slowRecent)).Should().BeTrue("1 hour is well inside a 30 day retention");
        (await Exists(slowAncient)).Should().BeFalse("31 days is past it");
    }

    [Test]
    public async Task Run_WithNoPerTrainRetentions_BehavesExactlyAsBefore()
    {
        // The whole whitelist collapses into one group, which is the shape every existing
        // deployment has. Guards against the grouping changing the default path.
        var cleanup = NewCleanup();
        cleanup.AddTrainType(FastTrain);
        cleanup.AddTrainType(SlowTrain);

        var old = await Seed(FastTrain, DateTime.UtcNow.AddHours(-1));
        var fresh = await Seed(SlowTrain, DateTime.UtcNow.AddMinutes(-5));

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        (await Exists(old)).Should().BeFalse();
        (await Exists(fresh)).Should().BeTrue();
    }

    [Test]
    public async Task Run_PerTrainRetention_CanAlsoBeShorterThanTheDefault()
    {
        var cleanup = NewCleanup(TimeSpan.FromDays(7));
        cleanup.AddTrainType(FastTrain, TimeSpan.FromMinutes(1));
        cleanup.AddTrainType(SlowTrain);

        var fast = await Seed(FastTrain, DateTime.UtcNow.AddHours(-1));
        var slow = await Seed(SlowTrain, DateTime.UtcNow.AddHours(-1));

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        (await Exists(fast)).Should().BeFalse();
        (await Exists(slow)).Should().BeTrue();
    }

    #endregion

    #region The runtime override (Decision 1)

    [Test]
    public async Task Run_RuntimeOverrideOfTheDefault_DoesNotShortenAPerTrainRetention()
    {
        // SchedulerConfigBootstrapHostedService and OperationsService both write
        // RetentionPeriod and nothing else, so a dashboard edit to "the retention" moves the
        // default group's cutoff and leaves a retention set in code alone. Asserted here so a
        // later change that folds the two together fails rather than quietly shortening it.
        var cleanup = NewCleanup();
        cleanup.AddTrainType(FastTrain);
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        var fast = await Seed(FastTrain, DateTime.UtcNow.AddMinutes(-10));
        var slow = await Seed(SlowTrain, DateTime.UtcNow.AddMinutes(-10));

        cleanup.RetentionPeriod = TimeSpan.FromMinutes(5);

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        (await Exists(fast)).Should().BeFalse("the override shortened the default group");
        (await Exists(slow))
            .Should()
            .BeTrue(
                "a retention set in code is not the default, so the runtime override must not "
                    + "shorten it. See docs/adr/0003-a-runtime-retention-override-replaces-only-the-default.md."
            );
    }

    #endregion

    #region Linked work queue rows

    [Test]
    public async Task Run_WorkQueueRowsFollowTheirMetadatasOwnCutoff()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        var kept = await Seed(SlowTrain, DateTime.UtcNow.AddHours(-1));
        var swept = await Seed(SlowTrain, DateTime.UtcNow.AddDays(-31));

        var keptQueue = await SeedWorkQueue(kept);
        var sweptQueue = await SeedWorkQueue(swept);

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.WorkQueues.AnyAsync(w => w.Id == keptQueue.Id))
            .Should()
            .BeTrue("its metadata is inside the 30 day retention");
        (await DataContext.WorkQueues.AnyAsync(w => w.Id == sweptQueue.Id))
            .Should()
            .BeFalse("its metadata was swept, and the queue row is owned by it");
    }

    #endregion

    #region Admin trains (Decision 2)

    [Test]
    public async Task Run_AdminTrainsArePrunedAtTheDefault_WhateverThePerTrainEntriesSay()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        var seeded = new List<long>();
        foreach (var adminName in AdminTrains.FullNames)
            seeded.Add((await Seed(adminName, DateTime.UtcNow.AddHours(-2))).Id);

        await BuildJunction(cleanup).Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.Where(m => seeded.Contains(m.Id)).CountAsync())
            .Should()
            .Be(
                0,
                "admin trains stay on the default retention whatever else is configured. See "
                    + "docs/adr/0003-a-runtime-retention-override-replaces-only-the-default.md."
            );
    }

    [Test]
    public void AddTrainType_AdminTrainWithItsOwnRetention_IsRefused()
    {
        var cleanup = NewCleanup();

        cleanup
            .Invoking(c => c.AddTrainType<ManifestManagerTrain>(TimeSpan.FromDays(1)))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*internal scheduler train*");
    }

    [Test]
    public void AddMetadataCleanup_StillAddsItsOwnAdminTrainsOnTheDefault()
    {
        // The refusal above must not catch AddMetadataCleanup's own two calls, which pass no
        // retention. If it did, every consumer calling AddMetadataCleanup() would throw.
        var cleanup = NewCleanup();

        cleanup.Invoking(c => c.AddTrainType<ManifestManagerTrain>()).Should().NotThrow();
        cleanup.Invoking(c => c.AddTrainType<MetadataCleanupTrain>()).Should().NotThrow();
    }

    #endregion

    #region Conflicting declarations

    [Test]
    public void AddTrainType_SameNameTwiceWithDifferentRetentions_IsRefused()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        cleanup
            .Invoking(c => c.AddTrainType(SlowTrain, TimeSpan.FromMinutes(5)))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already added*");
    }

    [Test]
    public void AddTrainType_SameNameTwiceWithTheSameRetention_IsAccepted()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(SlowTrain, TimeSpan.FromDays(30));

        cleanup.Invoking(c => c.AddTrainType(SlowTrain, TimeSpan.FromDays(30))).Should().NotThrow();

        cleanup.TrainTypeWhitelist.Should().ContainSingle();
    }

    [Test]
    public void AddTrainType_SameNameTwiceWithNoRetention_IsAcceptedAndNotDuplicated()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(SlowTrain);
        cleanup.AddTrainType(SlowTrain);

        cleanup.TrainTypeWhitelist.Should().ContainSingle();
    }

    [Test]
    public void AddTrainType_NonPositiveRetention_IsRefused()
    {
        var cleanup = NewCleanup();

        cleanup
            .Invoking(c => c.AddTrainType(SlowTrain, TimeSpan.Zero))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        cleanup
            .Invoking(c => c.AddTrainType(SlowTrain, TimeSpan.FromMinutes(-1)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Run_InterfaceAndClassNamesDisagree_KeepsTheLongerRetention()
    {
        // Not decidable at configuration time: these are two unrelated strings until discovery
        // relates them. The sweep resolves it by keeping the longer, so a row a consumer asked to
        // keep for 30 days is never deleted by the 30 minute group.
        var discovery = new FakeTrainDiscovery(
            typeof(IFakeRetentionTrain),
            typeof(FakeRetentionTrain)
        );

        var cleanup = NewCleanup();
        cleanup.AddTrainType(typeof(IFakeRetentionTrain).FullName!, TimeSpan.FromDays(30));
        cleanup.AddTrainType(typeof(FakeRetentionTrain).FullName!, TimeSpan.FromMinutes(1));

        var byInterface = await Seed(
            typeof(IFakeRetentionTrain).FullName!,
            DateTime.UtcNow.AddHours(-1)
        );
        var byClass = await Seed(
            typeof(FakeRetentionTrain).FullName!,
            DateTime.UtcNow.AddHours(-1)
        );

        await BuildJunction(cleanup, discovery).Run(new MetadataCleanupRequest());

        (await Exists(byInterface)).Should().BeTrue("the longer of the two retentions wins");
        (await Exists(byClass)).Should().BeTrue();
    }

    [Test]
    public async Task Validator_InterfaceAndClassNamesDisagree_RefusesAtStartup()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(typeof(IFakeRetentionTrain).FullName!, TimeSpan.FromDays(30));
        cleanup.AddTrainType(typeof(FakeRetentionTrain).FullName!, TimeSpan.FromMinutes(1));

        var validator = new MetadataCleanupConfigurationValidator(
            new SingleServiceProvider(
                new FakeTrainDiscovery(typeof(IFakeRetentionTrain), typeof(FakeRetentionTrain))
            ),
            new SchedulerConfiguration { MetadataCleanup = cleanup }
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflicting retention periods*");
    }

    [Test]
    public async Task Validator_ConsistentConfiguration_StartsCleanly()
    {
        var cleanup = NewCleanup();
        cleanup.AddTrainType(typeof(IFakeRetentionTrain).FullName!, TimeSpan.FromDays(30));

        var validator = new MetadataCleanupConfigurationValidator(
            new SingleServiceProvider(
                new FakeTrainDiscovery(typeof(IFakeRetentionTrain), typeof(FakeRetentionTrain))
            ),
            new SchedulerConfiguration { MetadataCleanup = cleanup }
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    #endregion

    #region Helpers

    private interface IFakeRetentionTrain { }

    private class FakeRetentionTrain : IFakeRetentionTrain { }

    private sealed class FakeTrainDiscovery(Type serviceType, Type implementationType)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() =>
            [
                new TrainRegistration
                {
                    ServiceType = serviceType,
                    ImplementationType = implementationType,
                    InputType = typeof(object),
                    OutputType = typeof(object),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = serviceType.Name,
                    ImplementationTypeName = implementationType.Name,
                    InputTypeName = "Object",
                    OutputTypeName = "Object",
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = false,
                    IsBroadcastEnabled = false,
                    IsRemote = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ];
    }

    /// <summary>Hands out one service and nothing else, which is all the validator resolves.</summary>
    private sealed class SingleServiceProvider(ITrainDiscoveryService discovery)
        : IServiceProvider,
            IServiceScopeFactory,
            IServiceScope
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ITrainDiscoveryService) ? discovery
            : serviceType == typeof(IServiceScopeFactory) ? this
            : null;

        public IServiceScope CreateScope() => this;

        public IServiceProvider ServiceProvider => this;

        public void Dispose() { }
    }

    private async Task<Metadata> Seed(string name, DateTime startTime)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        metadata.TrainState = TrainState.Completed;
        metadata.StartTime = startTime;
        metadata.EndTime = startTime.AddSeconds(1);

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<WorkQueue> SeedWorkQueue(Metadata metadata)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = metadata.Name,
                Input = null,
                InputTypeName = null,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);

        var entryId = entry.Id;
        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        return entry;
    }

    private async Task<bool> Exists(Metadata metadata)
    {
        DataContext.Reset();
        return await DataContext.Metadatas.AnyAsync(m => m.Id == metadata.Id);
    }

    #endregion
}
