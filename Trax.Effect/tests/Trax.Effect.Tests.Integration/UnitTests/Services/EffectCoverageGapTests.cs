using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Core.Train;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class EffectCoverageGapTests
{
    #region EffectJunction.RailwayJunction

    [Test]
    public async Task EffectJunction_RailwayJunction_NonServiceTrain_Throws()
    {
        var junction = new TestEffectJunction();
        var nonServiceTrain = new NonServiceTrain();

        Func<Task> act = async () =>
            await junction.RailwayJunction<string, string>(
                Either<Exception, string>.Right("in"),
                nonServiceTrain
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("*non-ServiceTrain*");
    }

    [Test]
    public async Task EffectJunction_RailwayJunction_NullMetadata_Throws()
    {
        var junction = new TestEffectJunction();
        var train = new TestTrain();
        // Leave Metadata null

        Func<Task> act = async () =>
            await junction.RailwayJunction(Either<Exception, string>.Right("in"), train);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Metadata cannot be null*");
    }

    [Test]
    public async Task EffectJunction_RailwayJunction_HappyPath_PopulatesMetadata()
    {
        var junction = new TestEffectJunction();
        var train = CreateTrain();

        var result = await junction.RailwayJunction(
            Either<Exception, string>.Right("hello"),
            train
        );

        result.IsRight.Should().BeTrue();
        result.IfRight(v => v.Should().Be("hello-out"));
        junction.Metadata.Should().NotBeNull();
        junction.Metadata!.Name.Should().Be(nameof(TestEffectJunction));
        junction.Metadata.HasRan.Should().BeTrue();
        junction.Metadata.StartTimeUtc.Should().NotBeNull();
        junction.Metadata.EndTimeUtc.Should().NotBeNull();
    }

    [Test]
    public async Task EffectJunction_RailwayJunction_WithEffectRunner_CallsBeforeAndAfter()
    {
        var runner = Substitute.For<IJunctionEffectRunner>();
        var junction = new TestEffectJunction();
        var train = CreateTrain(runner);

        await junction.RailwayJunction(Either<Exception, string>.Right("hi"), train);

        await runner
            .Received(1)
            .BeforeJunctionExecution(
                Arg.Any<EffectJunction<string, string>>(),
                Arg.Any<ServiceTrain<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        await runner
            .Received(1)
            .AfterJunctionExecution(
                Arg.Any<EffectJunction<string, string>>(),
                Arg.Any<ServiceTrain<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region JunctionEffectRunner.Before/AfterJunctionExecution

    [Test]
    public async Task JunctionEffectRunner_BeforeAndAfter_DispatchToActiveProviders()
    {
        var provider = Substitute.For<IJunctionEffectProvider>();
        var factory = Substitute.For<IJunctionEffectProviderFactory>();
        factory.Create().Returns(provider);

        var registry = Substitute.For<IEffectRegistry>();
        registry.IsEnabled(Arg.Any<Type>()).Returns(true);

        using var runner = new JunctionEffectRunner(new[] { factory }, registry);

        var junction = new TestEffectJunction();
        var train = CreateTrain();
        var ct = CancellationToken.None;

        await runner.BeforeJunctionExecution(junction, train, ct);
        await runner.AfterJunctionExecution(junction, train, ct);

        await provider.Received(1).BeforeJunctionExecution(junction, train, ct);
        await provider.Received(1).AfterJunctionExecution(junction, train, ct);
    }

    #endregion

    #region LifecycleHookRunner.OnStateChanged exception path

    [Test]
    public async Task LifecycleHookRunner_OnStateChanged_HookThrows_LogsAndContinues()
    {
        var hook1 = Substitute.For<ITrainLifecycleHook>();
        hook1
            .OnStateChanged(Arg.Any<Metadata>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("hook1 fail"));
        var hook2 = Substitute.For<ITrainLifecycleHook>();

        var f1 = Substitute.For<ITrainLifecycleHookFactory>();
        f1.Create().Returns(hook1);
        var f2 = Substitute.For<ITrainLifecycleHookFactory>();
        f2.Create().Returns(hook2);

        var registry = Substitute.For<IEffectRegistry>();
        registry.IsEnabled(Arg.Any<Type>()).Returns(true);

        using var runner = new LifecycleHookRunner(new[] { f1, f2 }, registry);

        var metadata = CreateMetadata();

        // Should not throw — hook1 fails, hook2 still runs.
        await runner.OnStateChanged(metadata, CancellationToken.None);

        await hook2.Received(1).OnStateChanged(metadata, Arg.Any<CancellationToken>());
    }

    #endregion

    #region ServiceExtensions — AddEffect / AddJunctionEffect / AddLifecycleHook overloads

    private static IServiceCollection ServicesWithEffectsConfigured(
        Action<TraxEffectBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                effects.UseInMemory();
                configure(effects);
                return effects;
            })
        );
        return services;
    }

    [Test]
    public void AddEffect_WithFactoryInstance_RegistersFactoryAndRegistry()
    {
        var fakeFactory = new FakeEffectProviderFactory();

        var services = ServicesWithEffectsConfigured(b => b.AddEffect(fakeFactory));

        services.Should().Contain(d => d.ServiceType == typeof(IEffectProviderFactory));
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEffectRegistry>();
        registry.IsEnabled(typeof(FakeEffectProviderFactory)).Should().BeTrue();
        registry.IsToggleable(typeof(FakeEffectProviderFactory)).Should().BeTrue();
    }

    [Test]
    public void AddJunctionEffect_WithIAndConcreteFactory_RegistersAllThreeServiceTypes()
    {
        var instance = new FakeJunctionEffectProviderFactory();

        var services = ServicesWithEffectsConfigured(b =>
            b.AddJunctionEffect<
                IFakeJunctionEffectProviderFactory,
                FakeJunctionEffectProviderFactory
            >(instance)
        );

        services.Should().Contain(d => d.ServiceType == typeof(FakeJunctionEffectProviderFactory));
        services.Should().Contain(d => d.ServiceType == typeof(IJunctionEffectProviderFactory));
        services.Should().Contain(d => d.ServiceType == typeof(IFakeJunctionEffectProviderFactory));
    }

    [Test]
    public void AddJunctionEffect_TypeOnly_RegistersConcreteAndInterface()
    {
        var services = ServicesWithEffectsConfigured(b =>
            b.AddJunctionEffect<FakeJunctionEffectProviderFactory>()
        );

        services.Should().Contain(d => d.ServiceType == typeof(FakeJunctionEffectProviderFactory));
        services.Should().Contain(d => d.ServiceType == typeof(IJunctionEffectProviderFactory));
    }

    [Test]
    public void AddJunctionEffect_TypeOnly_WithFactoryInstance_RegistersInterface()
    {
        var instance = new FakeJunctionEffectProviderFactory();

        var services = ServicesWithEffectsConfigured(b => b.AddJunctionEffect(instance));

        services.Should().Contain(d => d.ServiceType == typeof(IJunctionEffectProviderFactory));
    }

    [Test]
    public void AddLifecycleHook_WithIAndConcreteFactory_RegistersAllThreeServiceTypes()
    {
        var factory = new FakeLifecycleHookFactory();

        var services = ServicesWithEffectsConfigured(b =>
            b.AddLifecycleHook<IFakeLifecycleHookFactory, FakeLifecycleHookFactory>(factory)
        );

        services.Should().Contain(d => d.ServiceType == typeof(FakeLifecycleHookFactory));
        services.Should().Contain(d => d.ServiceType == typeof(ITrainLifecycleHookFactory));
        services.Should().Contain(d => d.ServiceType == typeof(IFakeLifecycleHookFactory));
    }

    [Test]
    public void AddLifecycleHook_WithFactoryInstance_RegistersFactory()
    {
        var factory = new FakeLifecycleHookFactory();

        var services = ServicesWithEffectsConfigured(b => b.AddLifecycleHook(factory));

        services.Should().Contain(d => d.ServiceType == typeof(ITrainLifecycleHookFactory));
    }

    [Test]
    public void AddLifecycleHook_TypeIsNeitherHookNorFactory_Throws()
    {
        Action act = () => ServicesWithEffectsConfigured(b => b.AddLifecycleHook<RandomType>());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ITrainLifecycleHook or ITrainLifecycleHookFactory*");
    }

    [Test]
    public void AddJunctionEffect_WithIAndConcreteFactory_ResolvesAllInterfacesToSameInstance()
    {
        var instance = new FakeJunctionEffectProviderFactory();
        var services = ServicesWithEffectsConfigured(b =>
            b.AddJunctionEffect<
                IFakeJunctionEffectProviderFactory,
                FakeJunctionEffectProviderFactory
            >(instance)
        );
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<FakeJunctionEffectProviderFactory>();
        var iJunctionEffect = provider.GetRequiredService<IJunctionEffectProviderFactory>();
        var iFakeFactory = provider.GetRequiredService<IFakeJunctionEffectProviderFactory>();

        concrete.Should().BeSameAs(instance);
        iJunctionEffect.Should().BeSameAs(instance);
        iFakeFactory.Should().BeSameAs(instance);
    }

    [Test]
    public void AddLifecycleHook_WithIAndConcreteFactory_ResolvesAllInterfacesToSameInstance()
    {
        var factory = new FakeLifecycleHookFactory();
        var services = ServicesWithEffectsConfigured(b =>
            b.AddLifecycleHook<IFakeLifecycleHookFactory, FakeLifecycleHookFactory>(factory)
        );
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<FakeLifecycleHookFactory>();
        var iTrain = provider.GetRequiredService<ITrainLifecycleHookFactory>();
        var iFake = provider.GetRequiredService<IFakeLifecycleHookFactory>();

        concrete.Should().BeSameAs(factory);
        iTrain.Should().BeSameAs(factory);
        iFake.Should().BeSameAs(factory);
    }

    [Test]
    public void AddScopedTraxJunction_RuntimeTypes_RegistersInterfaceAsScoped()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxJunction(typeof(IFakeRoute), typeof(FakeRoute));

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IFakeRoute) && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void AddTransientTraxJunction_RuntimeTypes_RegistersInterfaceAsTransient()
    {
        var services = new ServiceCollection();
        services.AddTransientTraxJunction(typeof(IFakeRoute), typeof(FakeRoute));

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IFakeRoute) && d.Lifetime == ServiceLifetime.Transient
            );
    }

    [Test]
    public void AddSingletonTraxJunction_RuntimeTypes_RegistersInterfaceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingletonTraxJunction(typeof(IFakeRoute), typeof(FakeRoute));

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IFakeRoute) && d.Lifetime == ServiceLifetime.Singleton
            );
    }

    #endregion

    #region Test helpers / fakes

    private static TestTrain CreateTrain(IJunctionEffectRunner? runner = null)
    {
        var train = new TestTrain();
        if (runner is not null)
            train.JunctionEffectRunner = runner;

        var metadataProp = typeof(ServiceTrain<string, string>).GetProperty(
            "Metadata",
            BindingFlags.Public | BindingFlags.Instance
        );
        metadataProp!.SetValue(train, CreateMetadata());

        return train;
    }

    private static Metadata CreateMetadata() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

    private class TestTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
    }

    private class TestEffectJunction : EffectJunction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input + "-out");
    }

    private class NonServiceTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
    }

    private class FakeEffectProviderFactory : IEffectProviderFactory
    {
        public IEffectProvider Create() => Substitute.For<IEffectProvider>();
    }

    public interface IFakeJunctionEffectProviderFactory : IJunctionEffectProviderFactory;

    public class FakeJunctionEffectProviderFactory : IFakeJunctionEffectProviderFactory
    {
        public IJunctionEffectProvider Create() => Substitute.For<IJunctionEffectProvider>();
    }

    public interface IFakeLifecycleHookFactory : ITrainLifecycleHookFactory;

    public class FakeLifecycleHookFactory : IFakeLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => Substitute.For<ITrainLifecycleHook>();
    }

    public class RandomType { }

    public interface IFakeRoute;

    public class FakeRoute : IFakeRoute;

    #endregion
}
