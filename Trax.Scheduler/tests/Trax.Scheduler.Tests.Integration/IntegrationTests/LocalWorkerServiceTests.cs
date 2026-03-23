using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.LocalWorkerService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="LocalWorkerService"/>, the background worker
/// that dequeues and executes jobs from the <c>trax.background_job</c> table.
/// </summary>
/// <remarks>
/// The LocalWorkerService uses PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c> for atomic,
/// lock-free dequeue across concurrent workers. These tests verify:
/// - Workers claim and execute jobs correctly
/// - Job rows are deleted after execution (both success and failure)
/// - Stale jobs (crashed workers) are reclaimed after visibility timeout
/// - Concurrent workers don't process the same job
/// - Graceful shutdown behavior
///
/// Since LocalWorkerService is a BackgroundService that starts automatically,
/// tests directly instantiate it with controlled options (single worker, fast polling)
/// for deterministic behavior.
/// </remarks>
[TestFixture]
public class LocalWorkerServiceTests : TestSetup
{
    #region Job Claim and Execute Tests

    [Test]
    public async Task Worker_ClaimsAndExecutes_AvailableJob()
    {
        // Arrange - Create a metadata record and a background job
        var metadata = await CreateMetadataForTestTrain();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var jobId = job.Id;

        // Act - Start a single worker and wait for it to process the job
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromMinutes(30),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
    public async Task Worker_ExecutesTrain_UpdatesMetadata()
    {
        // Arrange - Create manifest, metadata, and a background job pointing to it
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);
        var metadata = await CreateMetadataForManifest(manifest);

        var input = new SchedulerTestInput { Value = "worker-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
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
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - Metadata should be updated by the train execution
        DataContext.Reset();
        var updatedMetadata = await DataContext.Metadatas.FirstOrDefaultAsync(m =>
            m.Id == metadata.Id
        );

        updatedMetadata.Should().NotBeNull();
        // The JobRunnerTrain should have run the train
        updatedMetadata!.TrainState.Should().NotBe(TrainState.Pending);
    }

    #endregion

    #region Job Deletion Tests

    [Test]
    public async Task Worker_DeletesJob_AfterSuccessfulExecution()
    {
        // Arrange
        var metadata = await CreateMetadataForTestTrain();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
        // Arrange - Create a metadata pointing to a train that will fail
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
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
        var metadata = await CreateMetadataForTestTrain();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        // Set FetchedAt to simulate a worker that crashed 2 seconds ago
        job.FetchedAt = DateTime.UtcNow.AddSeconds(-2);

        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act - Start worker with a very short visibility timeout (1 second)
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromSeconds(1),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
        var metadata = await CreateMetadataForTestTrain();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
        // Set FetchedAt to now (within visibility timeout of 30m)
        job.FetchedAt = DateTime.UtcNow;

        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Act - Start worker with default visibility timeout (30m)
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            VisibilityTimeout = TimeSpan.FromMinutes(30),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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
            var metadata = await CreateMetadataForTestTrain();
            metadataIds.Add(metadata.Id);

            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Start multiple workers
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 3,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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

    #region Batch Claim Tests

    [Test]
    public async Task Worker_BatchSize1_ClaimsOneJobPerRound()
    {
        // Arrange - Create 5 jobs
        for (var i = 0; i < 5; i++)
        {
            var metadata = await CreateMetadataForTestTrain();
            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Start single worker with BatchSize=1 (default)
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 1,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - All 5 jobs should have been processed
        DataContext.Reset();
        var remaining = await DataContext.BackgroundJobs.CountAsync();
        remaining.Should().Be(0, "all jobs should be processed with BatchSize=1");
    }

    [Test]
    public async Task Worker_BatchSize5_ClaimsMultipleJobsPerRound()
    {
        // Arrange - Create 10 jobs
        for (var i = 0; i < 10; i++)
        {
            var metadata = await CreateMetadataForTestTrain();
            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Start single worker with BatchSize=5
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 5,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - All 10 jobs should have been processed
        DataContext.Reset();
        var remaining = await DataContext.BackgroundJobs.CountAsync();
        remaining.Should().Be(0, "all jobs should be processed with BatchSize=5");
    }

    [Test]
    public async Task Worker_BatchSize_LargerThanAvailable_ClaimsAllAvailable()
    {
        // Arrange - Create only 3 jobs but set BatchSize=10
        for (var i = 0; i < 3; i++)
        {
            var metadata = await CreateMetadataForTestTrain();
            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(2000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - All 3 available jobs should be processed
        DataContext.Reset();
        var remaining = await DataContext.BackgroundJobs.CountAsync();
        remaining
            .Should()
            .Be(0, "all available jobs should be claimed even when BatchSize > available");
    }

    #endregion

    #region Input Deserialization Tests

    [Test]
    public async Task Worker_WithInputJob_DeserializesAndPassesToTrain()
    {
        // Arrange - Create a job with serialized input
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);
        var metadata = await CreateMetadataForManifest(manifest);

        var input = new SchedulerTestInput { Value = "deserialization-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
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
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
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

    #region Priority Ordering Tests

    [Test]
    public async Task Worker_ClaimsHighPriorityJobs_BeforeLowPriority()
    {
        // Arrange - Create jobs with priorities 0, 15, 31 in order low→high
        // so created_at favors low priority (created first)
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);

        var input = new SchedulerTestInput { Value = "priority-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var metaLow = await CreateMetadataForManifest(manifest);
        var jobLow = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metaLow.Id,
                Priority = 0,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(jobLow);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Task.Delay(50); // ensure distinct created_at

        var metaMed = await CreateMetadataForManifest(manifest);
        var jobMed = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metaMed.Id,
                Priority = 15,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(jobMed);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Task.Delay(50);

        var metaHigh = await CreateMetadataForManifest(manifest);
        var jobHigh = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metaHigh.Id,
                Priority = 31,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(jobHigh);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act - Single worker with BatchSize=3 claims all at once
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 3,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - All jobs processed; high-priority should complete first (sequential in batch)
        DataContext.Reset();
        var remaining = await DataContext.BackgroundJobs.CountAsync();
        remaining.Should().Be(0, "all jobs should be processed");

        // Verify execution order via metadata EndTime — single worker executes sequentially
        // so earlier EndTime means processed first
        var metadatas = await DataContext
            .Metadatas.Where(m => m.Id == metaLow.Id || m.Id == metaMed.Id || m.Id == metaHigh.Id)
            .ToListAsync();

        var highMeta = metadatas.First(m => m.Id == metaHigh.Id);
        var medMeta = metadatas.First(m => m.Id == metaMed.Id);
        var lowMeta = metadatas.First(m => m.Id == metaLow.Id);

        highMeta
            .EndTime.Should()
            .NotBeNull("high-priority job should have completed")
            .And.BeBefore(
                medMeta.EndTime!.Value,
                "high-priority job should complete before medium"
            );
        medMeta
            .EndTime.Should()
            .BeBefore(lowMeta.EndTime!.Value, "medium-priority job should complete before low");
    }

    [Test]
    public async Task Worker_ClaimsFIFO_WithinSamePriority()
    {
        // Arrange - 3 jobs all with priority 10, created at different times
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);

        var input = new SchedulerTestInput { Value = "fifo-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var meta1 = await CreateMetadataForManifest(manifest);
        var job1 = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = meta1.Id,
                Priority = 10,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(job1);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Task.Delay(50);

        var meta2 = await CreateMetadataForManifest(manifest);
        var job2 = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = meta2.Id,
                Priority = 10,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(job2);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Task.Delay(50);

        var meta3 = await CreateMetadataForManifest(manifest);
        var job3 = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = meta3.Id,
                Priority = 10,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(job3);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 3,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - FIFO within same priority: job1 completes before job2 before job3
        DataContext.Reset();
        var metadatas = await DataContext
            .Metadatas.Where(m => m.Id == meta1.Id || m.Id == meta2.Id || m.Id == meta3.Id)
            .ToListAsync();

        var m1 = metadatas.First(m => m.Id == meta1.Id);
        var m2 = metadatas.First(m => m.Id == meta2.Id);
        var m3 = metadatas.First(m => m.Id == meta3.Id);

        m1.EndTime.Should()
            .NotBeNull()
            .And.BeOnOrBefore(m2.EndTime!.Value, "FIFO: job1 should complete before job2");
        m2.EndTime.Should()
            .BeOnOrBefore(m3.EndTime!.Value, "FIFO: job2 should complete before job3");
    }

    [Test]
    public async Task Worker_DefaultPriority_IsZero()
    {
        // Arrange - Create a background job without explicit priority
        var metadata = await CreateMetadataForTestTrain();
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });

        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        DataContext.Reset();

        // Assert - Priority should default to 0
        var savedJob = await DataContext.BackgroundJobs.FirstAsync(j => j.Id == jobId);
        savedJob.Priority.Should().Be(0, "default priority should be 0");
    }

    [Test]
    public async Task Worker_MixedPriorities_HighPriorityProcessedFirst_EvenIfCreatedLater()
    {
        // Arrange - Low priority created first, high priority created 100ms later
        var group = await CreateAndSaveManifestGroup(DataContext);
        var manifest = await CreateAndSaveManifest(group);

        var input = new SchedulerTestInput { Value = "mixed-priority-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var metaLow = await CreateMetadataForManifest(manifest);
        var jobLow = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metaLow.Id,
                Priority = 0,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(jobLow);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Task.Delay(100);

        var metaHigh = await CreateMetadataForManifest(manifest);
        var jobHigh = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metaHigh.Id,
                Priority = 31,
                Input = inputJson,
                InputType = typeof(SchedulerTestInput).FullName,
            }
        );
        await DataContext.Track(jobHigh);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Act
        using var cts = new CancellationTokenSource();
        var options = new LocalWorkerOptions
        {
            WorkerCount = 1,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 2,
        };

        var workerService = new LocalWorkerService(
            Scope.ServiceProvider,
            options,
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>()
        );

        var workerTask = workerService.StartAsync(cts.Token);
        await Task.Delay(3000);
        cts.Cancel();

        try
        {
            await workerTask;
        }
        catch (OperationCanceledException) { }

        // Assert - High priority job should complete first despite being created later
        DataContext.Reset();
        var highMeta = await DataContext.Metadatas.FirstAsync(m => m.Id == metaHigh.Id);
        var lowMeta = await DataContext.Metadatas.FirstAsync(m => m.Id == metaLow.Id);

        highMeta.EndTime.Should().NotBeNull("high-priority job should have completed");
        lowMeta.EndTime.Should().NotBeNull("low-priority job should have completed");
        highMeta
            .EndTime!.Value.Should()
            .BeBefore(
                lowMeta.EndTime!.Value,
                "high-priority job should execute before low-priority despite being created later"
            );
    }

    #endregion

    #region Helper Methods

    private async Task<Metadata> CreateMetadataForTestTrain()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
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
                Name = typeof(SchedulerTestTrain),
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
                Name = typeof(FailingSchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 0,
                Properties = new FailingSchedulerTestInput
                {
                    FailureMessage = "Expected test failure",
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
