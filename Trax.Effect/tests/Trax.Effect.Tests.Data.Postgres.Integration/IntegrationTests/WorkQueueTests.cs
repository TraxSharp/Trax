using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using ManifestGroup = Trax.Effect.Models.ManifestGroup.ManifestGroup;

namespace Trax.Effect.Tests.Data.Postgres.Integration.IntegrationTests;

public class WorkQueueTests : TestSetup
{
    [Theory]
    public async Task CreateWorkQueue_PersistsToDatabase()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.TrainName.Should().Be("Trax.Core.Tests.TestTrain");
        found.Input.Should().Be("""{"value": "test"}""");
        found.InputTypeName.Should().Be("Trax.Core.Tests.TestInput");
        found.Status.Should().Be(WorkQueueStatus.Queued);
        found.ExternalId.Should().NotBeNullOrEmpty();
        found.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        found.DispatchedAt.Should().BeNull();
        found.ManifestId.Should().BeNull();
        found.MetadataId.Should().BeNull();
        found.Priority.Should().Be(0);
    }

    [Theory]
    public async Task CreateWorkQueue_WithPriority_PersistsToDatabase()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"priority-test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
                Priority = 15,
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Priority.Should().Be(15);
    }

    [Theory]
    public async Task CreateWorkQueue_WithOverflowPriority_ClampsToMax()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"clamp-test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
                Priority = 50,
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Priority.Should().Be(WorkQueue.MaxPriority);
    }

    [Theory]
    public async Task CreateWorkQueue_WithNegativePriority_ClampsToZero()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"negative-test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
                Priority = -5,
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Priority.Should().Be(WorkQueue.MinPriority);
    }

    [Theory]
    public async Task CreateWorkQueue_WithManifestLink_PersistsRelationship()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var manifestGroup = new ManifestGroup
        {
            Name = $"test-group-{Guid.NewGuid():N}",
            Priority = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.ManifestGroups.Add(manifestGroup);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(WorkQueueTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        manifest.ManifestGroupId = manifestGroup.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(WorkQueueTests).FullName!,
                Input = """{"value":"test"}""",
                InputTypeName = typeof(WorkQueueTests).FullName,
                ManifestId = manifest.Id,
            }
        );

        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var found = await context
            .WorkQueues.Include(q => q.Manifest)
            .FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.ManifestId.Should().Be(manifest.Id);
        found.Manifest.Should().NotBeNull();
        found.Manifest!.Id.Should().Be(manifest.Id);
    }

    [Theory]
    public async Task CreateWorkQueue_WithoutManifest_PersistsWithNullManifestId()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.AdHocTrain",
                Input = """{"key":"value"}""",
                InputTypeName = "Trax.Core.Tests.AdHocInput",
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.ManifestId.Should().BeNull();
        found.Manifest.Should().BeNull();
    }

    [Theory]
    public async Task UpdateStatus_ToDispatched_PersistsWithMetadataLink()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"dispatch-test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
            }
        );

        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.Core.Tests.TestTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new { value = "dispatch-test" },
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Act
        entry.Status = WorkQueueStatus.Dispatched;
        entry.MetadataId = metadata.Id;
        entry.DispatchedAt = DateTime.UtcNow;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context
            .WorkQueues.Include(q => q.Metadata)
            .FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Status.Should().Be(WorkQueueStatus.Dispatched);
        found.MetadataId.Should().Be(metadata.Id);
        found.DispatchedAt.Should().NotBeNull();
        found.Metadata.Should().NotBeNull();
        found.Metadata!.Id.Should().Be(metadata.Id);
    }

    [Theory]
    public async Task UpdateStatus_ToCancelled_Persists()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.TestTrain",
                Input = """{"value":"cancel-test"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
            }
        );

        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        var entryId = entry.Id;

        // Act
        entry.Status = WorkQueueStatus.Cancelled;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entryId);

        // Assert
        found.Should().NotBeNull();
        found!.Status.Should().Be(WorkQueueStatus.Cancelled);
        found.DispatchedAt.Should().BeNull();
        found.MetadataId.Should().BeNull();
    }

    [Theory]
    public async Task QueryByStatus_ReturnsOnlyMatchingEntries()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var queuedEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.QueuedTrain",
                Input = """{"status":"queued"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
            }
        );

        var cancelledEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.CancelledTrain",
                Input = """{"status":"cancelled"}""",
                InputTypeName = "Trax.Core.Tests.TestInput",
            }
        );

        await context.Track(queuedEntry);
        await context.Track(cancelledEntry);
        await context.SaveChanges(CancellationToken.None);

        cancelledEntry.Status = WorkQueueStatus.Cancelled;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var queuedEntries = await context
            .WorkQueues.Where(q => q.Status == WorkQueueStatus.Queued)
            .ToListAsync();

        var cancelledEntries = await context
            .WorkQueues.Where(q => q.Status == WorkQueueStatus.Cancelled)
            .ToListAsync();

        // Assert
        queuedEntries.Should().Contain(q => q.Id == queuedEntry.Id);
        queuedEntries.Should().NotContain(q => q.Id == cancelledEntry.Id);
        cancelledEntries.Should().Contain(q => q.Id == cancelledEntry.Id);
    }

    [Theory]
    public async Task InputJsonb_RoundTrips()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var complexJson =
            """{"name":"test","nested":{"key":"value","numbers":[1,2,3]},"enabled":true}""";

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Core.Tests.JsonTestTrain",
                Input = complexJson,
                InputTypeName = "Trax.Core.Tests.ComplexInput",
            }
        );

        // Act
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == entry.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Input.Should().NotBeNull();
        // JSONB normalizes whitespace but preserves data
        found.Input.Should().Contain("\"name\"");
        found.Input.Should().Contain("\"test\"");
        found.Input.Should().Contain("\"nested\"");
        found.Input.Should().Contain("[1, 2, 3]");
        found.Input.Should().Contain("true");
    }
}
