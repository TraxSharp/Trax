using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class AddLifecycleHookTests
{
    #region Type-Only Registration

    [Test]
    public void AddLifecycleHook_TypeOnly_RegistersFactoryAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options => options.AddLifecycleHook<StubHookFactory>());
        using var provider = services.BuildServiceProvider();

        var factories = provider.GetServices<ITrainLifecycleHookFactory>().ToList();

        factories.Should().ContainSingle(f => f is StubHookFactory);
    }

    [Test]
    public void AddLifecycleHook_TypeOnly_FactoryResolvableByConcreteType()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options => options.AddLifecycleHook<StubHookFactory>());
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetService<StubHookFactory>();

        factory.Should().NotBeNull();
    }

    [Test]
    public void AddLifecycleHook_TypeOnly_RegistersInEffectRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options => options.AddLifecycleHook<StubHookFactory>());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IEffectRegistry>();

        registry.IsEnabled(typeof(StubHookFactory)).Should().BeTrue();
        registry.IsToggleable(typeof(StubHookFactory)).Should().BeTrue();
    }

    [Test]
    public void AddLifecycleHook_NonToggleable_RegisteredAsNonToggleable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options =>
            options.AddLifecycleHook<StubHookFactory>(toggleable: false)
        );
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IEffectRegistry>();

        registry.IsToggleable(typeof(StubHookFactory)).Should().BeFalse();
    }

    #endregion

    #region Instance Registration

    [Test]
    public void AddLifecycleHook_Instance_RegistersProvidedFactory()
    {
        var instance = new StubHookFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options => options.AddLifecycleHook(instance));
        using var provider = services.BuildServiceProvider();

        var factories = provider.GetServices<ITrainLifecycleHookFactory>().ToList();

        factories.Should().ContainSingle(f => ReferenceEquals(f, instance));
    }

    #endregion

    #region LifecycleHookRunner Resolution

    [Test]
    public void LifecycleHookRunner_ResolvedFromDI_IncludesRegisteredHooks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options => options.AddLifecycleHook<StubHookFactory>());
        using var provider = services.BuildServiceProvider();

        var runner = provider.GetService<ILifecycleHookRunner>();

        runner.Should().NotBeNull();
    }

    [Test]
    public void LifecycleHookRunner_NoHooksRegistered_StillResolvable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(_ => { });
        using var provider = services.BuildServiceProvider();

        var runner = provider.GetService<ILifecycleHookRunner>();

        runner.Should().NotBeNull();
    }

    #endregion

    #region Multiple Hooks

    [Test]
    public void AddLifecycleHook_Multiple_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options =>
            options.AddLifecycleHook<StubHookFactory>().AddLifecycleHook<AnotherStubHookFactory>()
        );
        using var provider = services.BuildServiceProvider();

        var factories = provider.GetServices<ITrainLifecycleHookFactory>().ToList();

        factories.Should().HaveCount(2);
    }

    #endregion

    #region Test Stubs

    private class StubHook : ITrainLifecycleHook { }

    private class StubHookFactory : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => new StubHook();
    }

    private class AnotherStubHookFactory : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => new StubHook();
    }

    #endregion
}
