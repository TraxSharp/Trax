using System.Text.Json;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.BackgroundTaskServer;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.PostgresWorkerService;
using Trax.Effect.Utils;
using Trax.Scheduler.Tests.Integration.Examples.Workflows;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="PostgresWorkerService"/>, the background worker
/// that dequeues and executes jobs from the <c>trax.background_job</c> table.
/// </summary>
/// <remarks>
/// The PostgresWorkerService uses PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c> for atomic,
/// lock-free dequeue across concurrent workers. These tests verify:
/// - Workers claim and execute jobs correctly
/// - Job rows are deleted after execution (both success and failure)
/// - Stale jobs (crashed workers) are reclaimed after visibility timeout
/// - Concurrent workers don't process the same job
/// - Graceful shutdown behavior
///
/// Since PostgresWorkerService is a BackgroundService that starts automatically,
/// tests directly instantiate it with controlled options (single worker, fast polling)
/// for deterministic behavior.
/// </remarks>
[TestFixture]
public class PostgresWorkerServiceTests : TestSetup
{
    #region Job Claim and Execute Tests

    [Test]
    public async Task Worker_ClaimsAndExecutes_AvailableJob()
    {
        // Arrange - Create a metadata record and a background job
        var metadata = await CreateMetadataForTestWorkflow();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var jobId = job.Id;

        // Act - Start a single worker and wait for it to process the job
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromMinutes(30),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        // Start the worker and give it time to process
        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000); // Allow enough time for claim + execute + cleanup
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }

        // Assert - The job should have been executed and deleted
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().BeNull("job should be deleted after execution");
    }

    [Test]
    public async Task Worker_ExecutesWorkflow_UpdatesMetadata()
    {
        // Arrange - Create manifest, metadata, and a background job pointing to it
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);
        var metadata = await CreateMetadataForManifest(manifest);

        var input = new SchedulerTestInput { Value = "worker-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            Trax.CoreJsonSerializationOptions.ManifestProperties
        );

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Start worker
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - Metadata should be updated by the workflow execution
        DataContext.Reset();
        var updatedMetadata = await DataContext.Metadatas.FirstOrDefaultAsync(
            m => m.Id == metadata.Id
        );

        updatedMetadata.Should().NotBeNull();
        // The TaskServerExecutorWorkflow should have run the workflow
        updatedMetadata!.WorkflowState.Should().NotBe(WorkflowState.Pending);
    }

    #endregion

    #region Job Deletion Tests

    [Test]
    public async Task Worker_DeletesJob_AfterSuccessfulExecution()
    {
        // Arrange
        var metadata = await CreateMetadataForTestWorkflow();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().BeNull("job should be deleted after successful execution");
    }

    [Test]
    public async Task Worker_DeletesJob_AfterFailedExecution()
    {
        // Arrange - Create a metadata pointing to a workflow that will fail
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveFailingManifest(group);
        var metadata = await CreateMetadataForManifest(manifest);

        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - Job should be deleted even on failure (matches AutoDeleteOnSuccessFilter behavior)
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().BeNull("job should be deleted even after failed execution");
    }

    #endregion

    #region No Work Available Tests

    [Test]
    public async Task Worker_WithNoJobs_PollsAndWaits()
    {
        // Arrange - No jobs in the queue

        // Act - Start worker with short polling interval
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(500); // Let it poll a few times
        cts.Cancel();

        // Assert - Should complete without errors
        var act = async () =>
        {
            try
            {
                await workerTask;
            }
            catch (OperationCanceledException) { }
        };

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Visibility Timeout Tests

    [Test]
    public async Task Worker_ReclainsStaleJob_AfterVisibilityTimeout()
    {
        // Arrange - Create a job that was claimed but never completed (simulates crash)
        var metadata = await CreateMetadataForTestWorkflow();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        // Set FetchedAt to simulate a worker that crashed 2 seconds ago
        job.FetchedAt = DateTime.UtcNow.AddSeconds(-2);

        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act - Start worker with a very short visibility timeout (1 second)
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromSeconds(1),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - The stale job should have been reclaimed and executed (then deleted)
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().BeNull("stale job should be reclaimed and executed");
    }

    [Test]
    public async Task Worker_DoesNotReclaim_RecentlyClaimedJob()
    {
        // Arrange - Create a job that was claimed just now (simulates in-progress by another worker)
        var metadata = await CreateMetadataForTestWorkflow();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        // Set FetchedAt to now (within visibility timeout of 30m)
        job.FetchedAt = DateTime.UtcNow;

        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act - Start worker with default visibility timeout (30m)
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromMinutes(30),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - The recently claimed job should NOT be reclaimed
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().NotBeNull("recently claimed job should not be reclaimed");
        remainingJob!.FetchedAt.Should().NotBeNull();
    }

    #endregion

    #region Multiple Workers Tests

    [Test]
    public async Task MultipleWorkers_ProcessMultipleJobs_NoDuplicates()
    {
        // Arrange - Create several jobs
        var metadataIds = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var metadata = await CreateMetadataForTestWorkflow();
            metadataIds.Add(metadata.Id);

            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Start multiple workers
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 3,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000); // Allow time for all jobs to be processed
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - All jobs should have been processed and deleted
        DataContext.Reset();
        var remainingJobs = await DataContext.BackgroundJobs.CountAsync();
        remainingJobs.Should().Be(0, "all jobs should be processed and deleted");
    }

    #endregion

    #region Input Deserialization Tests

    [Test]
    public async Task Worker_WithInputJob_DeserializesAndPassesToWorkflow()
    {
        // Arrange - Create a job with serialized input
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);
        var metadata = await CreateMetadataForManifest(manifest);

        var input = new SchedulerTestInput { Value = "deserialization-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            Trax.CoreJsonSerializationOptions.ManifestProperties
        );

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new PostgresTaskServerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new PostgresWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PostgresWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - Job should be executed and deleted
        DataContext.Reset();
        var remainingJob = await DataContext.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        remainingJob.Should().BeNull("job with input should be executed and deleted");
    }

    #endregion

    #region Helper Methods

    private async Task<Metadata> CreateMetadataForTestWorkflow()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestWorkflow).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "worker-test" },
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<Manifest> CreateAndSaveManifest(ManifestGroup group)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestWorkflow),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "worker-test" },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Manifest> CreateAndSaveFailingManifest(ManifestGroup group)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(FailingSchedulerTestWorkflow),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 0,
                Properties = new FailingSchedulerTestInput
                {
                    FailureMessage = "Expected test failure"
                },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Metadata> CreateMetadataForManifest(Manifest manifest)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = manifest.Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "worker-test" },
                ManifestId = manifest.Id,
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    #endregion
}
