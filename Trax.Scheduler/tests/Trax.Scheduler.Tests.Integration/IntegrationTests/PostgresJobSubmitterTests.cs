using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Examples.Trains;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="PostgresJobSubmitter"/>, the built-in PostgreSQL
/// implementation of <see cref="IJobSubmitter"/>.
/// </summary>
/// <remarks>
/// PostgresJobSubmitter enqueues jobs by inserting rows into the <c>trax.background_job</c>
/// table. These tests verify:
/// - EnqueueAsync(metadataId) creates a BackgroundJob with correct MetadataId
/// - EnqueueAsync(metadataId, input) serializes input and stores the type name
/// - Returns a valid job ID (the database-generated primary key)
/// - Multiple enqueues create separate rows
/// </remarks>
[TestFixture]
public class PostgresJobSubmitterTests : TestSetup
{
    #region EnqueueAsync(metadataId) Tests

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_CreatesBackgroundJob()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 42);

        // Assert
        jobId.Should().NotBeNullOrEmpty();

        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.MetadataId.Should().Be(42);
        job.Input.Should().BeNull();
        job.InputType.Should().BeNull();
        job.FetchedAt.Should().BeNull();
        job.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_ReturnsStringId()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 1);

        // Assert
        int.TryParse(jobId, out var parsed)
            .Should()
            .BeTrue("job ID should be a parseable integer");
        parsed.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task EnqueueAsync_CalledMultipleTimes_CreatesDistinctJobs()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);

        // Act
        var jobId1 = await jobSubmitter.EnqueueAsync(metadataId: 10);
        var jobId2 = await jobSubmitter.EnqueueAsync(metadataId: 20);
        var jobId3 = await jobSubmitter.EnqueueAsync(metadataId: 30);

        // Assert
        jobId1.Should().NotBe(jobId2);
        jobId2.Should().NotBe(jobId3);

        DataContext.Reset();
        var jobs = await DataContext
            .BackgroundJobs.Where(j =>
                new[] { long.Parse(jobId1), long.Parse(jobId2), long.Parse(jobId3) }.Contains(j.Id)
            )
            .ToListAsync();

        jobs.Should().HaveCount(3);
        jobs.Select(j => j.MetadataId).Should().BeEquivalentTo([10, 20, 30]);
    }

    #endregion

    #region EnqueueAsync(metadataId, input) Tests

    [Test]
    public async Task EnqueueAsync_WithInput_SerializesInputToJson()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);
        var input = new SchedulerTestInput { Value = "hello-world" };

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 50, input: input);

        // Assert
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.MetadataId.Should().Be(50);
        job.Input.Should().NotBeNull();
        job.Input.Should().Contain("hello-world");
        job.InputType.Should().NotBeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithInput_StoresFullTypeName()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);
        var input = new SchedulerTestInput { Value = "type-test" };

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 51, input: input);

        // Assert
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.InputType.Should().Contain("SchedulerTestInput");
    }

    [Test]
    public async Task EnqueueAsync_WithInput_InputCanBeDeserialized()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);
        var input = new SchedulerTestInput { Value = "round-trip-test" };

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 52, input: input);

        // Assert
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.Input.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<SchedulerTestInput>(
            job.Input!,
            TraxJsonSerializationOptions.ManifestProperties
        );

        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be("round-trip-test");
    }

    [Test]
    public async Task EnqueueAsync_WithComplexInput_SerializesCorrectly()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);
        var input = new SchedulerTestInput { Value = "complex with special chars: <>&\"'" };

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 53, input: input);

        // Assert
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.Input.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<SchedulerTestInput>(
            job.Input!,
            TraxJsonSerializationOptions.ManifestProperties
        );

        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be("complex with special chars: <>&\"'");
    }

    #endregion

    #region Job Lifecycle Tests

    [Test]
    public async Task EnqueueAsync_CreatedJob_HasNullFetchedAt()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 60);

        // Assert - Newly enqueued jobs should be available for dequeue (FetchedAt == null)
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.FetchedAt.Should().BeNull("newly enqueued jobs should be available for worker claim");
    }

    [Test]
    public async Task EnqueueAsync_CreatedJob_HasRecentCreatedAt()
    {
        // Arrange
        var jobSubmitter = new PostgresJobSubmitter(DataContext);
        var beforeEnqueue = DateTime.UtcNow;

        // Act
        var jobId = await jobSubmitter.EnqueueAsync(metadataId: 61);

        // Assert
        DataContext.Reset();
        var job = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j =>
            j.Id == int.Parse(jobId)
        );

        job.Should().NotBeNull();
        job!.CreatedAt.Should().BeOnOrAfter(beforeEnqueue.AddSeconds(-1));
        job.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    #endregion
}
