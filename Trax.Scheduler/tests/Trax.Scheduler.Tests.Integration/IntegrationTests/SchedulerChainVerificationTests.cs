using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Every scheduler host runs the startup chain check over the scheduler's own trains, so a
/// chain regression in the ManifestManager, the JobDispatcher or the JobRunner would stop every
/// such host from starting. Each is read and replayed here against a real Postgres registration,
/// which constructs them without connecting.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class SchedulerChainVerificationTests
{
    private static ServiceProvider BuildProvider()
    {
        var connectionString = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build()
            .GetRequiredSection("Configuration")["DatabaseConnectionString"]!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(connectionString))
                .AddMediator(typeof(JobRunnerTrain).Assembly)
                .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
        );

        return services.BuildServiceProvider();
    }

    [TestCase(typeof(IManifestManagerTrain), typeof(Unit), typeof(Unit))]
    [TestCase(typeof(IJobDispatcherTrain), typeof(Unit), typeof(Unit))]
    [TestCase(typeof(IJobRunnerTrain), typeof(RunJobRequest), typeof(Unit))]
    public void A_scheduler_train_declares_a_chain_that_can_run(
        Type service,
        Type input,
        Type output
    )
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var isService = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

        var train = scope.ServiceProvider.GetRequiredService(service);
        var chain = (ChainRecorder)train.GetType().GetMethod("DeclaredChain")!.Invoke(train, null)!;

        chain.Steps.Should().NotBeEmpty("the train must actually declare its chain");
        ChainVerification
            .Verify(chain, input, output, isService.IsService)
            .Should()
            .BeEmpty(
                $"{service.Name}'s chain must line up, or every scheduler host refuses to start. "
                    + "See Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md"
            );
    }
}
