using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.LocalWorkerService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A stored input type name (a work queue row's <c>input_type_name</c>, a background job's
/// <c>input_type</c>) is resolved only among the registered trains' input types, the same as a
/// runner request's (scheduler/0006). A name that is not one of them is refused before anything
/// is constructed from it.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
[NonParallelizable]
public class StoredInputTypeResolutionTests : TestSetup
{
    /// <summary>
    /// A loaded type that no train takes. Counts its constructions, so a test can tell whether
    /// the stored JSON was deserialized into it.
    /// </summary>
    public sealed class NotATrainInput
    {
        private static int _constructed;

        public static int Constructed => Volatile.Read(ref _constructed);

        public static void ResetCount() => Interlocked.Exchange(ref _constructed, 0);

        public NotATrainInput() => Interlocked.Increment(ref _constructed);

        public string? Value { get; set; }
    }

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        NotATrainInput.ResetCount();
    }

    [Test]
    public async Task Dispatch_refuses_a_work_queue_row_whose_input_type_no_train_takes()
    {
        var entry = await SaveWorkQueueEntry(typeof(NotATrainInput).FullName!);

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        row.Status.Should().Be(WorkQueueStatus.Queued);
        row.MetadataId.Should().BeNull();
        (await DataContext.Metadatas.CountAsync(m => m.Name == typeof(SchedulerTestTrain).FullName))
            .Should()
            .Be(0);
        NotATrainInput
            .Constructed.Should()
            .Be(
                0,
                "a stored input type name is resolved only among registered train inputs (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Dispatch_resolves_a_registered_input_type_by_full_or_assembly_qualified_name(
        bool assemblyQualified
    )
    {
        var entry = await SaveWorkQueueEntry(
            assemblyQualified
                ? typeof(SchedulerTestInput).AssemblyQualifiedName!
                : typeof(SchedulerTestInput).FullName!
        );

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        row.Status.Should().Be(WorkQueueStatus.Dispatched);
        row.MetadataId.Should().NotBeNull();
    }

    [Test]
    public async Task Local_worker_refuses_a_background_job_whose_input_type_no_train_takes()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "stored-type" },
            }
        );
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = """{"value":"x"}""",
                InputType = typeof(NotATrainInput).FullName,
            }
        );
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        using var cts = new CancellationTokenSource();
        var worker = new LocalWorkerService(
            Scope.ServiceProvider,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(100),
            },
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>(),
            Scope.ServiceProvider.GetRequiredService<ISqlDialect>()
        );

        await worker.StartAsync(cts.Token);
        var drained = await WaitForJobAbsent(job.Id, TimeSpan.FromSeconds(15));
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        drained.Should().BeTrue("the worker deletes a job it could not run");
        NotATrainInput.Constructed.Should().Be(0);
        DataContext.Reset();
        var after = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);
        after.TrainState.Should().Be(TrainState.Pending);
    }

    private async Task<WorkQueue> SaveWorkQueueEntry(string inputTypeName)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = """{"value":"stored-type"}""",
                InputTypeName = inputTypeName,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry;
    }

    private async Task RunDispatcher()
    {
        using var scope = Scope.ServiceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        await train.Run(Unit.Default);
    }

    private async Task<bool> WaitForJobAbsent(long jobId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            DataContext.Reset();
            if (
                await DataContext.BackgroundJobs.FirstOrDefaultAsync(
                    j => j.Id == jobId,
                    CancellationToken.None
                )
                is null
            )
                return true;
            try
            {
                // allowed-delay: the pause between polls of the completion condition
                await Task.Delay(50, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return false;
    }
}
