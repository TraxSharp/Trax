using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class MediatorBuilderConcurrencyTests
{
    #region GlobalConcurrentRunLimit

    [Test]
    public void GlobalConcurrentRunLimit_SetsValue()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .GlobalConcurrentRunLimit(50)
                )
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<MediatorConfiguration>();
        config.GlobalMaxConcurrentRun.Should().Be(50);
    }

    [Test]
    public void GlobalConcurrentRunLimit_Zero_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects)
                    .AddMediator(mediator =>
                        mediator
                            .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                            .GlobalConcurrentRunLimit(0)
                    )
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ConcurrentRunLimit<TTrain>

    [Test]
    public void ConcurrentRunLimit_SetsPerTrainOverride()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .ConcurrentRunLimit<IMemoryTestTrain>(15)
                )
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<MediatorConfiguration>();
        config.ConcurrencyOverrides.Should().ContainKey(typeof(IMemoryTestTrain).FullName!);
        config.ConcurrencyOverrides[typeof(IMemoryTestTrain).FullName!].Should().Be(15);
    }

    [Test]
    public void ConcurrentRunLimit_Multiple_AddsAll()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .ConcurrentRunLimit<IMemoryTestTrain>(15)
                        .ConcurrentRunLimit<IFailingTestTrain>(5)
                )
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<MediatorConfiguration>();
        config.ConcurrencyOverrides.Should().HaveCount(2);
        config.ConcurrencyOverrides[typeof(IMemoryTestTrain).FullName!].Should().Be(15);
        config.ConcurrencyOverrides[typeof(IFailingTestTrain).FullName!].Should().Be(5);
    }

    [Test]
    public void ConcurrentRunLimit_Zero_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects)
                    .AddMediator(mediator =>
                        mediator
                            .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                            .ConcurrentRunLimit<IMemoryTestTrain>(0)
                    )
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Defaults

    [Test]
    public void Build_NoConcurrencyConfig_DefaultsToNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<MediatorConfiguration>();
        config.GlobalMaxConcurrentRun.Should().BeNull();
        config.ConcurrencyOverrides.Should().BeEmpty();
    }

    #endregion

    #region DI Registration

    [Test]
    public void IConcurrencyLimiter_RegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        var limiter = provider.GetService<IConcurrencyLimiter>();
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<ConcurrencyLimiter>();
    }

    #endregion
}
