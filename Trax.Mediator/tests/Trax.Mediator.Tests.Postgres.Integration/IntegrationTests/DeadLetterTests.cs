using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;
using ManifestGroup = Trax.Effect.Models.ManifestGroup.ManifestGroup;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

public class DeadLetterTests : TestSetup
{
    private static async Task<ManifestGroup> CreateTestManifestGroup(
        IDataContext context,
        string name = "test-group"
    )
    {
        var group = new ManifestGroup
        {
            Name = $"{name}-{Guid.NewGuid():N}",
            Priority = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.ManifestGroups.Add(group);
        await context.SaveChanges(CancellationToken.None);
        return group;
    }

    [Theory]
    public async Task TestPostgresProviderCanCreateDeadLetter()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        // Create a manifest record first (the job definition)
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        // Create a dead letter for the failed execution
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );

        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var foundDeadLetter = await context.DeadLetters.FirstOrDefaultAsync(x =>
            x.ManifestId == manifest.Id
        );

        // Assert
        foundDeadLetter.Should().NotBeNull();
        foundDeadLetter!.ManifestId.Should().Be(manifest.Id);
        foundDeadLetter.Reason.Should().Be("Max retries exceeded");
        foundDeadLetter.RetryCountAtDeadLetter.Should().Be(3);
        foundDeadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
    }

    [Theory]
    public async Task TestDeadLetterCanBeLinkedToManifest()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        // Create a manifest record
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        // Create a dead letter
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Non-retryable exception",
                RetryCount = 1,
            }
        );

        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var foundDeadLetter = await context
            .DeadLetters.Include(d => d.Manifest)
            .FirstOrDefaultAsync(x => x.Id == deadLetter.Id);

        // Assert
        foundDeadLetter.Should().NotBeNull();
        foundDeadLetter!.Manifest.Should().NotBeNull();
        foundDeadLetter.Manifest!.Id.Should().Be(manifest.Id);
        foundDeadLetter.Manifest.Name.Should().Be(typeof(DeadLetterTests).FullName);
    }

    [Theory]
    public async Task TestDeadLetterAcknowledgePersists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        // Create manifest and dead letter
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Test reason",
                RetryCount = 2,
            }
        );
        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        var deadLetterId = deadLetter.Id;
        context.Reset();

        // Act - Acknowledge the dead letter
        var foundDeadLetter = await context.DeadLetters.FirstOrDefaultAsync(x =>
            x.Id == deadLetterId
        );
        foundDeadLetter.Should().NotBeNull();

        foundDeadLetter!.Acknowledge("Issue resolved manually");
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var acknowledgedDeadLetter = await context.DeadLetters.FirstOrDefaultAsync(x =>
            x.Id == deadLetterId
        );

        acknowledgedDeadLetter.Should().NotBeNull();
        acknowledgedDeadLetter!.Status.Should().Be(DeadLetterStatus.Acknowledged);
        acknowledgedDeadLetter.ResolutionNote.Should().Be("Issue resolved manually");
        acknowledgedDeadLetter.ResolvedAt.Should().NotBeNull();
    }

    [Theory]
    public async Task TestDeadLetterMarkRetriedPersists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        // Create manifest
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        // Create dead letter
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );
        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        var deadLetterId = deadLetter.Id;

        // Create retry metadata
        var retryMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = nameof(TestDeadLetterMarkRetriedPersists) + "_Retry",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new { Test = "value", Retry = true },
            }
        );

        await context.Track(retryMetadata);
        await context.SaveChanges(CancellationToken.None);
        var retryMetadataId = retryMetadata.Id;
        context.Reset();

        // Act - Mark as retried
        var foundDeadLetter = await context.DeadLetters.FirstOrDefaultAsync(x =>
            x.Id == deadLetterId
        );
        foundDeadLetter.Should().NotBeNull();

        foundDeadLetter!.MarkRetried(retryMetadataId);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var retriedDeadLetter = await context
            .DeadLetters.Include(d => d.RetryMetadata)
            .FirstOrDefaultAsync(x => x.Id == deadLetterId);

        retriedDeadLetter.Should().NotBeNull();
        retriedDeadLetter!.Status.Should().Be(DeadLetterStatus.Retried);
        retriedDeadLetter.RetryMetadataId.Should().Be(retryMetadataId);
        retriedDeadLetter.ResolvedAt.Should().NotBeNull();
        retriedDeadLetter.RetryMetadata.Should().NotBeNull();
        retriedDeadLetter.RetryMetadata!.Id.Should().Be(retryMetadataId);
    }

    [Theory]
    public async Task TestDeadLetterRequeuePersists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );
        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        var deadLetterId = deadLetter.Id;
        context.Reset();

        // Act
        var foundDeadLetter = await context.DeadLetters.FirstAsync(x => x.Id == deadLetterId);
        foundDeadLetter.Requeue("Re-queued via dashboard (WorkQueue 42)");
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var requeuedDeadLetter = await context.DeadLetters.FirstAsync(x => x.Id == deadLetterId);
        requeuedDeadLetter.Status.Should().Be(DeadLetterStatus.Retried);
        requeuedDeadLetter.ResolutionNote.Should().Be("Re-queued via dashboard (WorkQueue 42)");
        requeuedDeadLetter.ResolvedAt.Should().NotBeNull();
        requeuedDeadLetter.RetryMetadataId.Should().BeNull();
    }

    [Theory]
    public async Task TestDeadLetterLinkRetryMetadataPersists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );
        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);

        // Create retry metadata
        var retryMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = nameof(TestDeadLetterLinkRetryMetadataPersists),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new { Test = "value" },
            }
        );
        await context.Track(retryMetadata);
        await context.SaveChanges(CancellationToken.None);
        var deadLetterId = deadLetter.Id;
        var retryMetadataId = retryMetadata.Id;
        context.Reset();

        // Act — Requeue first, then link metadata (the real-world two-phase flow)
        var foundDeadLetter = await context.DeadLetters.FirstAsync(x => x.Id == deadLetterId);
        foundDeadLetter.Requeue("Re-queued via service");
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var foundAgain = await context.DeadLetters.FirstAsync(x => x.Id == deadLetterId);
        foundAgain.LinkRetryMetadata(retryMetadataId);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var linkedDeadLetter = await context
            .DeadLetters.Include(d => d.RetryMetadata)
            .FirstAsync(x => x.Id == deadLetterId);
        linkedDeadLetter.Status.Should().Be(DeadLetterStatus.Retried);
        linkedDeadLetter.RetryMetadataId.Should().Be(retryMetadataId);
        linkedDeadLetter.RetryMetadata.Should().NotBeNull();
    }

    [Theory]
    public async Task TestWorkQueueDeadLetterIdPersists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;
        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );
        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);

        // Create WorkQueue entry with DeadLetterId
        var workQueue = Effect.Models.WorkQueue.WorkQueue.Create(
            new Effect.Models.WorkQueue.DTOs.CreateWorkQueue
            {
                TrainName = typeof(DeadLetterTests).FullName!,
                ManifestId = manifest.Id,
                DeadLetterId = deadLetter.Id,
            }
        );
        await context.Track(workQueue);
        await context.SaveChanges(CancellationToken.None);
        var workQueueId = workQueue.Id;
        context.Reset();

        // Assert
        var foundWorkQueue = await context
            .WorkQueues.Include(w => w.DeadLetter)
            .FirstAsync(w => w.Id == workQueueId);
        foundWorkQueue.DeadLetterId.Should().Be(deadLetter.Id);
        foundWorkQueue.DeadLetter.Should().NotBeNull();
    }

    [Theory]
    public async Task TestCanQueryDeadLettersByStatus()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = await CreateTestManifestGroup(context);

        // Create manifests for multiple dead letters
        var manifest1 = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest1.ManifestGroupId = manifestGroup.Id;
        var manifest2 = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest2.ManifestGroupId = manifestGroup.Id;
        var manifest3 = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest3.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest1);
        await context.Track(manifest2);
        await context.Track(manifest3);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter1 = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest1,
                Reason = "Reason 1",
                RetryCount = 1,
            }
        );
        var deadLetter2 = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest2,
                Reason = "Reason 2",
                RetryCount = 2,
            }
        );
        deadLetter2.Acknowledge("Acknowledged");
        var deadLetter3 = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest3,
                Reason = "Reason 3",
                RetryCount = 3,
            }
        );

        await context.Track(deadLetter1);
        await context.Track(deadLetter2);
        await context.Track(deadLetter3);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var awaitingIntervention = await context
            .DeadLetters.Where(d => d.Status == DeadLetterStatus.AwaitingIntervention)
            .ToListAsync();

        var acknowledged = await context
            .DeadLetters.Where(d => d.Status == DeadLetterStatus.Acknowledged)
            .ToListAsync();

        // Assert
        awaitingIntervention.Should().Contain(d => d.ManifestId == manifest1.Id);
        awaitingIntervention.Should().Contain(d => d.ManifestId == manifest3.Id);
        acknowledged.Should().Contain(d => d.ManifestId == manifest2.Id);
    }
}
