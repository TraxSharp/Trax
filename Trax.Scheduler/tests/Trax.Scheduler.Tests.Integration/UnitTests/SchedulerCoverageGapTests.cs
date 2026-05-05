using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Tests for the validation throws inside SchedulerConfigurationBuilder.InferredScheduling.
/// These error paths fail-fast at builder time when the developer mis-pairs train and input types,
/// or uses a class that doesn't implement IServiceTrain.
/// </summary>
[TestFixture]
public class SchedulerCoverageGapTests
{
    private static SchedulerConfiguration ResolveConfiguration(
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
    public void Schedule_Inferred_InputTypeMismatch_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.Schedule<ISchedulerTestTrain>("ext", new MismatchedInput(), Every.Minutes(5))
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Input type mismatch*");
    }

    [Test]
    public void ScheduleMany_Inferred_ItemInputTypeMismatch_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.ScheduleMany<ISchedulerTestTrain>(
                    new[]
                    {
                        new ManifestItem("a", new SchedulerTestInput()),
                        new ManifestItem("b", new MismatchedInput()),
                    },
                    Every.Minutes(5)
                )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Input type mismatch*");
    }

    [Test]
    public void Schedule_Inferred_NonServiceTrainType_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.Schedule<NotATrain>("ext", new SchedulerTestInput(), Every.Minutes(5))
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*must implement IServiceTrain*");
    }

    [Test]
    public void ThenIncludeMany_NameBased_AppliesPrefixAndGroup()
    {
        // Covers the ThenIncludeMany<TTrain>(name, items, options) overload that delegates
        // to the parameterless overload after applying the prefix/group.
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .ThenIncludeMany<ISchedulerTestTrain>(
                    "downstream",
                    new[]
                    {
                        new ManifestItem("alpha", new SchedulerTestInput()) { DependsOn = "root" },
                        new ManifestItem("beta", new SchedulerTestInput()) { DependsOn = "root" },
                    }
                )
        );

        var batch = config.PendingManifests.Last();
        batch.ExpectedExternalIds.Should().Equal("downstream-alpha", "downstream-beta");
    }

    public record MismatchedInput : IManifestProperties;

    public class NotATrain { }
}
