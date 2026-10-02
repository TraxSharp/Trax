using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Core.Decisions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.DecisionRecording;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A host that runs trains which ask a decider, without AddDecisionRecording, could run them but
/// would fail every requeue that replays recorded decisions. It refuses to start, naming every
/// such train, rather than leaving the first replay to find out.
/// </summary>
[TestFixture]
public class DecisionRecordingStartupCheckTests
{
    [Test]
    public async Task A_host_without_decision_recording_refuses_to_start_naming_the_trains_that_decide()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
                Configure(
                    services,
                    recordDecisions: false,
                    typeof(AssemblyMarker).Assembly,
                    typeof(JobRunnerTrain).Assembly
                )
            )
            .Build();

        var refusal = (
            await host.Invoking(h => h.StartAsync())
                .Should()
                .ThrowAsync<InvalidOperationException>()
        ).Which;

        refusal.Message.Should().Contain(typeof(IDecisionProbeTrain).FullName!);
        refusal.Message.Should().Contain("AddDecisionRecording()");
        refusal
            .Message.Should()
            .NotContain(
                typeof(ISchedulerTestTrain).FullName!,
                "a train that asks no decider has nothing to replay"
            );
    }

    [Test]
    public async Task A_host_that_records_decisions_passes_the_check()
    {
        await using var provider = Build(
            recordDecisions: true,
            typeof(AssemblyMarker).Assembly,
            typeof(JobRunnerTrain).Assembly
        );

        await CheckOver(provider)
            .Invoking(check => check.StartingAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task A_host_whose_trains_do_not_decide_passes_the_check_without_recording()
    {
        // Only the scheduler's own trains, none of which asks a decider.
        await using var provider = Build(recordDecisions: false, typeof(JobRunnerTrain).Assembly);

        await CheckOver(provider)
            .Invoking(check => check.StartingAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task A_check_started_only_through_StartAsync_still_refuses()
    {
        await using var provider = Build(
            recordDecisions: false,
            typeof(AssemblyMarker).Assembly,
            typeof(JobRunnerTrain).Assembly
        );

        await CheckOver(provider)
            .Invoking(check => check.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{typeof(IDecisionProbeTrain).FullName}*");
    }

    [Test]
    public void Every_host_that_runs_trains_has_the_check_once_and_first()
    {
        var scheduler = Configure(
            new ServiceCollection(),
            recordDecisions: false,
            typeof(AssemblyMarker).Assembly,
            typeof(JobRunnerTrain).Assembly
        );
        scheduler.AddTraxJobRunner();

        var runner = new ServiceCollection().AddLogging();
        runner.AddTrax(trax =>
            trax.AddEffects(effects => effects).AddMediator(typeof(AssemblyMarker).Assembly)
        );
        runner.AddTraxJobRunner();

        var worker = new ServiceCollection().AddLogging();
        worker.AddTrax(trax =>
            trax.AddEffects(effects => effects).AddMediator(typeof(AssemblyMarker).Assembly)
        );
        worker.AddTraxWorker();

        foreach (var services in new[] { scheduler, runner, worker })
        {
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

            hosted.Count(IsCheck).Should().Be(1);
            hosted
                .TakeWhile(d => !IsCheck(d))
                .Select(d => d.ImplementationType?.Assembly)
                .Should()
                .NotContain(
                    typeof(JobRunnerTrain).Assembly,
                    "no scheduler service may start before the check refuses the host"
                );
        }
    }

    private static bool IsCheck(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType == typeof(DecisionRecordingStartupCheck);

    private static DecisionRecordingStartupCheck CheckOver(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IServiceProviderIsService>(),
            provider.GetRequiredService<ITrainDiscoveryService>()
        );

    private static ServiceProvider Build(bool recordDecisions, params Assembly[] trains) =>
        Configure(new ServiceCollection(), recordDecisions, trains).BuildServiceProvider();

    private static IServiceCollection Configure(
        IServiceCollection services,
        bool recordDecisions,
        params Assembly[] trains
    )
    {
        // The mediator's chain verification needs a decider for the probe train's chain.
        services.AddLogging().AddSingleton(Substitute.For<IDecider>());
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                {
                    var data = effects.UsePostgres(TestPostgres.ConnectionString);
                    return recordDecisions ? data.AddDecisionRecording() : data;
                })
                .AddMediator(trains)
                .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
        );
        return services;
    }
}
