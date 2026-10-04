using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A host that stops while a job is being submitted cancels the dispatcher's token. The claim
/// was already committed, so the failed submit must still be recorded: the run's row Failed and
/// the entry requeued, rather than a Pending row and a Dispatched entry holding the subject until
/// the stale-pending reaper finds them.
/// </summary>
[TestFixture]
public class DispatchShutdownTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IDataContext _dataContext = null!;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        _serviceProvider = new ServiceCollection()
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, ShutdownDuringSubmit>()
                        )
                    )
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _serviceProvider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        await TestSetup.CleanupDatabase(_dataContext);
    }

    [TearDown]
    public void TestTearDown()
    {
        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
    }

    [Test]
    public async Task A_submit_cancelled_by_shutdown_is_recorded_as_a_failed_attempt_and_requeued()
    {
        var entry = await Queue();

        using var stopping = new CancellationTokenSource();
        ShutdownDuringSubmit.Stopping = stopping;

        using (var cycleScope = _serviceProvider.CreateScope())
        {
            var train = cycleScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
            try
            {
                await train.Run(Unit.Default, stopping.Token);
            }
            catch (OperationCanceledException)
            {
                // The dispatcher's own run ends cancelled; that is not what this test is about.
            }
        }

        stopping.IsCancellationRequested.Should().BeTrue("the submitter stopped the host");

        _dataContext.Reset();
        var queued = await _dataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == entry.Id);
        queued.Status.Should().Be(WorkQueueStatus.Queued, "the job was never delivered");
        queued.MetadataId.Should().BeNull();

        var run = await _dataContext
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.ManifestId == entry.ManifestId);
        run.TrainState.Should().Be(TrainState.Failed, "the attempt is recorded, not left Pending");
    }

    private async Task<WorkQueue> Queue()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            _dataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "shutdown" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await _dataContext.Track(manifest);
        await _dataContext.SaveChanges(CancellationToken.None);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = manifest.Properties,
                InputTypeName = typeof(SchedulerTestInput).FullName,
                ManifestId = manifest.Id,
            }
        );
        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();
        return entry;
    }

    /// <summary>A submitter during whose submit the host begins to stop.</summary>
    private sealed class ShutdownDuringSubmit : IJobSubmitter
    {
        public static CancellationTokenSource Stopping { get; set; } = new();

        public Task<string> EnqueueAsync(long metadataId) =>
            EnqueueAsync(metadataId, CancellationToken.None);

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            EnqueueAsync(metadataId, CancellationToken.None);

        public Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken)
        {
            Stopping.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The dispatcher did not pass its token.");
        }

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        ) => EnqueueAsync(metadataId, cancellationToken);
    }
}
