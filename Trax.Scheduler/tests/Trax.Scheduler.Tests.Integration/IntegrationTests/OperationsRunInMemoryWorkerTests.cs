using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// With in-memory workers the submitter runs the train before it returns, so a train that fails
/// makes the submit throw after the run has already recorded its outcome. That is a run that was
/// submitted and failed, not a server that could not start it.
/// </summary>
[TestFixture]
public class OperationsRunInMemoryWorkerTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        _provider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _provider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        using var scope = _provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [Test]
    public async Task A_failing_train_is_a_successful_submit_with_a_failed_run()
    {
        using var scope = _provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();

        var result = await operations.RunTrainAsync(
            new RunTrainInput(
                typeof(IFailingSchedulerTestTrain).FullName!,
                """{"failureMessage":"the train failed"}"""
            ),
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);

        using var check = _provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<IDataContext>();
        var run = await db.Metadatas.AsNoTracking().SingleAsync(m => m.Id == result.Id);
        run.TrainState.Should().Be(TrainState.Failed, "the run records the train's failure");
        run.FailureReason.Should().Contain("the train failed");
    }
}
