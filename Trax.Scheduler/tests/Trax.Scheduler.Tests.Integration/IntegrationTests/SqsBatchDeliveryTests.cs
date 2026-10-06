using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Utils;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Lambda;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// An SQS-triggered runner processes every record in a batch and reports back only the records
/// that could not be delivered to their train, so one bad record neither stops the rest nor sends
/// records that already ran back to the queue.
/// </summary>
[TestFixture]
public class SqsBatchDeliveryTests
{
    private ServiceProvider _serviceProvider = null!;
    private IDataContext _dataContext = null!;
    private IServiceScope _scope = null!;
    private readonly List<string> _keys = [];

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        var services = new ServiceCollection()
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly)
            );
        services.AddTraxJobRunner(runner => runner.AllowUnsignedRequests());
        services.AddScoped<IDataContext>(sp =>
            (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
        );
        _serviceProvider = services.BuildServiceProvider();
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
        foreach (var key in _keys)
            DeliveryProbeTrain.Forget(key);
        _keys.Clear();
        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
    }

    [Test]
    public async Task Only_the_record_that_could_not_be_delivered_is_reported()
    {
        var (first, firstKey) = await PendingRun();
        var (second, _) = await PendingRun();
        var (third, thirdKey) = await PendingRun(fail: true);

        var handler = new SqsJobRunnerHandler(_serviceProvider);
        var batch = new SQSEvent
        {
            Records =
            [
                Record("m1", first, firstKey),
                // Its input type is not one this runner has, so its train is never started.
                Record("m2", second, "unused", inputType: "Some.Unknown.Input"),
                Record("m3", third, thirdKey),
            ],
        };

        var response = await handler.HandleBatchAsync(batch);

        response
            .BatchItemFailures.Select(f => f.ItemIdentifier)
            .Should()
            .Equal(["m2"], "only m2 did not reach its train; m3 ran and its failure is recorded");
        DeliveryProbeTrain.Runs[firstKey].Should().Be(1);
        DeliveryProbeTrain.Runs[thirdKey].Should().Be(1, "a record after a bad one still runs");

        _dataContext.Reset();
        (await StateOf(second)).Should().Be(TrainState.Pending);
        (await StateOf(third)).Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task A_record_naming_a_run_that_does_not_exist_is_reported()
    {
        var (run, key) = await PendingRun();
        var handler = new SqsJobRunnerHandler(_serviceProvider);
        var batch = new SQSEvent
        {
            Records = [Record("m1", run, key), Record("m2", run + 1_000_000, "missing")],
        };

        var response = await handler.HandleBatchAsync(batch);

        response
            .BatchItemFailures.Select(f => f.ItemIdentifier)
            .Should()
            .Equal(["m2"], "no row records m2's outcome, so it is not acknowledged");
        DeliveryProbeTrain.Runs[key].Should().Be(1);
    }

    [Test]
    public async Task A_redelivered_record_whose_run_already_completed_is_acknowledged()
    {
        var (run, key) = await PendingRun();
        var handler = new SqsJobRunnerHandler(_serviceProvider);
        var batch = new SQSEvent { Records = [Record("m1", run, key)] };

        (await handler.HandleBatchAsync(batch)).BatchItemFailures.Should().BeEmpty();
        var redelivered = await handler.HandleBatchAsync(batch);

        redelivered
            .BatchItemFailures.Should()
            .BeEmpty("the run is done; there is nothing to retry");
        DeliveryProbeTrain.Runs[key].Should().Be(1);
    }

    [Test]
    public async Task HandleAsync_runs_every_record_before_asking_for_the_batch_to_be_retried()
    {
        var (first, firstKey) = await PendingRun();
        var (second, _) = await PendingRun();
        var (third, thirdKey) = await PendingRun();

        var handler = new SqsJobRunnerHandler(_serviceProvider);
        var batch = new SQSEvent
        {
            Records =
            [
                Record("m1", first, firstKey),
                Record("m2", second, "unused", inputType: "Some.Unknown.Input"),
                Record("m3", third, thirdKey),
            ],
        };

        var act = async () => await handler.HandleAsync(batch);

        await act.Should().ThrowAsync<Exception>("m2 must be delivered again");
        DeliveryProbeTrain.Runs[firstKey].Should().Be(1);
        DeliveryProbeTrain.Runs.ContainsKey(thirdKey).Should().BeTrue("m3 is not held back by m2");
    }

    private async Task<TrainState> StateOf(long metadataId) =>
        (
            await _dataContext.Metadatas.AsNoTracking().SingleAsync(m => m.Id == metadataId)
        ).TrainState;

    private async Task<(long MetadataId, string Key)> PendingRun(bool fail = false)
    {
        var key = Guid.NewGuid().ToString("N");
        _keys.Add(key);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IDeliveryProbeTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await _dataContext.Track(metadata);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();
        _probeInputs[key] = new DeliveryProbeInput { Key = key, Fail = fail };
        return (metadata.Id, key);
    }

    private readonly Dictionary<string, DeliveryProbeInput> _probeInputs = [];

    private SQSEvent.SQSMessage Record(
        string messageId,
        long metadataId,
        string key,
        string? inputType = null
    )
    {
        var input = _probeInputs.TryGetValue(key, out var known)
            ? known
            : new DeliveryProbeInput { Key = key };
        var request = new RemoteJobRequest(
            metadataId,
            JsonSerializer.Serialize(input, TraxJsonSerializationOptions.ManifestProperties),
            inputType ?? typeof(DeliveryProbeInput).FullName
        );
        return new SQSEvent.SQSMessage
        {
            MessageId = messageId,
            Body = JsonSerializer.Serialize(request),
        };
    }
}
