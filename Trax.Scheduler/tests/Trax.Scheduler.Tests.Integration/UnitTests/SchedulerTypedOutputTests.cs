using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Tests that the scheduler supports trains with non-Unit output types.
/// The scheduler never uses the output — it is discarded by the execution pipeline.
/// These tests verify that the Unit constraint has been fully removed.
/// </summary>
[TestFixture]
public class SchedulerTypedOutputTests
{
    private SchedulerConfiguration ResolveConfiguration(
        Action<SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    scheduler.UseInMemoryWorkers();
                    configure(scheduler);
                    return scheduler;
                })
        );
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SchedulerConfiguration>();
    }

    [Test]
    public void Schedule_InferredOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act — should not throw
        var config = ResolveConfiguration(scheduler =>
            scheduler.Schedule<ITypedOutputSchedulerTestTrain>(
                "typed-output-test",
                new TypedOutputSchedulerTestInput { Value = "hello" },
                Every.Minutes(5)
            )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("typed-output-test");
    }

    [Test]
    public void Schedule_ExplicitOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act — should not throw
        var config = ResolveConfiguration(scheduler =>
            scheduler.Schedule<
                ITypedOutputSchedulerTestTrain,
                TypedOutputSchedulerTestInput,
                string
            >(
                "typed-output-explicit",
                new TypedOutputSchedulerTestInput { Value = "hello" },
                Every.Minutes(5)
            )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("typed-output-explicit");
    }

    [Test]
    public void ScheduleMany_InferredOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act
        var config = ResolveConfiguration(scheduler =>
            scheduler.ScheduleMany<ITypedOutputSchedulerTestTrain>(
                "typed-batch",
                new[]
                {
                    new ManifestItem("a", new TypedOutputSchedulerTestInput { Value = "a" }),
                    new ManifestItem("b", new TypedOutputSchedulerTestInput { Value = "b" }),
                },
                Every.Minutes(5)
            )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(1);
        config
            .PendingManifests[0]
            .ExpectedExternalIds.Should()
            .BeEquivalentTo(["typed-batch-a", "typed-batch-b"]);
    }

    [Test]
    public void ScheduleOnce_InferredOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act
        var config = ResolveConfiguration(scheduler =>
            scheduler.ScheduleOnce<ITypedOutputSchedulerTestTrain>(
                "typed-once",
                new TypedOutputSchedulerTestInput { Value = "once" },
                TimeSpan.FromMinutes(10)
            )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("typed-once");
    }

    [Test]
    public void Schedule_UnitOutputTrain_StillWorks()
    {
        // Verify backward compatibility — Unit output trains still work
        var config = ResolveConfiguration(scheduler =>
            scheduler.Schedule<ISchedulerTestTrain>(
                "unit-test",
                new SchedulerTestInput { Value = "hello" },
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("unit-test");
    }

    [Test]
    public void Schedule_ExplicitOverload_UnitOutputTrain_StillWorks()
    {
        // Verify backward compatibility with explicit type parameters
        var config = ResolveConfiguration(scheduler =>
            scheduler.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "unit-explicit",
                new SchedulerTestInput { Value = "hello" },
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("unit-explicit");
    }

    [Test]
    public void Include_InferredOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act
        var config = ResolveConfiguration(scheduler =>
            scheduler
                .Schedule<ISchedulerTestTrain>(
                    "root",
                    new SchedulerTestInput { Value = "root" },
                    Every.Minutes(5)
                )
                .Include<ITypedOutputSchedulerTestTrain>(
                    "typed-dependent",
                    new TypedOutputSchedulerTestInput { Value = "dep" }
                )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(2);
    }

    [Test]
    public void ThenInclude_InferredOverload_WorksWithTypedOutputTrain()
    {
        // Arrange & Act
        var config = ResolveConfiguration(scheduler =>
            scheduler
                .Schedule<ISchedulerTestTrain>(
                    "root",
                    new SchedulerTestInput { Value = "root" },
                    Every.Minutes(5)
                )
                .ThenInclude<ITypedOutputSchedulerTestTrain>(
                    "typed-chain",
                    new TypedOutputSchedulerTestInput { Value = "chain" }
                )
        );

        // Assert
        config.PendingManifests.Should().HaveCount(2);
    }
}
