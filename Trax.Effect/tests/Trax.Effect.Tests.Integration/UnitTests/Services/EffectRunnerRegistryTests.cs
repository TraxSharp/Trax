using Trax.Effect.Models;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.StepEffectProvider;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.Services.StepEffectRunner;
using FluentAssertions;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class EffectRunnerRegistryTests
{
    #region EffectRunner

    [Test]
    public void EffectRunner_DisabledFactory_SkipsCreateCall()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new DisabledEffectFactory();
        registry.Register(typeof(DisabledEffectFactory), enabled: false);

        // Act
        using var runner = new EffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void EffectRunner_EnabledFactory_CallsCreate()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new EnabledEffectFactory();
        registry.Register(typeof(EnabledEffectFactory), enabled: true);

        // Act
        using var runner = new EffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void EffectRunner_UntrackedFactory_CallsCreate()
    {
        // Arrange - factory type not registered in registry (infrastructure effect)
        var registry = new EffectRegistry();
        var factory = new EnabledEffectFactory();
        // Intentionally NOT registering the factory type

        // Act
        using var runner = new EffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void EffectRunner_MixOfEnabledAndDisabled_OnlyCreatesEnabled()
    {
        // Arrange - use concrete stub types so GetType() returns distinct types
        var registry = new EffectRegistry();
        var enabledFactory = new EnabledEffectFactory();
        var disabledFactory = new DisabledEffectFactory();

        registry.Register(typeof(EnabledEffectFactory), enabled: true);
        registry.Register(typeof(DisabledEffectFactory), enabled: false);

        // Act
        using var runner = new EffectRunner([enabledFactory, disabledFactory], registry);

        // Assert
        enabledFactory.CreateCalled.Should().BeTrue();
        disabledFactory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void EffectRunner_NonToggleableFactory_CannotBeDisabled()
    {
        // Arrange - register as non-toggleable, then try to disable
        var registry = new EffectRegistry();
        var factory = new EnabledEffectFactory();
        registry.Register(typeof(EnabledEffectFactory), enabled: true, toggleable: false);

        // Attempt to disable should be a no-op
        registry.Disable(typeof(EnabledEffectFactory));

        // Act
        using var runner = new EffectRunner([factory], registry);

        // Assert - factory should still run because it can't be disabled
        factory.CreateCalled.Should().BeTrue();
    }

    #endregion

    #region StepEffectRunner

    [Test]
    public void StepEffectRunner_DisabledFactory_SkipsCreateCall()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new DisabledStepEffectFactory();
        registry.Register(typeof(DisabledStepEffectFactory), enabled: false);

        // Act
        using var runner = new StepEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void StepEffectRunner_EnabledFactory_CallsCreate()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new EnabledStepEffectFactory();
        registry.Register(typeof(EnabledStepEffectFactory), enabled: true);

        // Act
        using var runner = new StepEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void StepEffectRunner_UntrackedFactory_CallsCreate()
    {
        // Arrange - factory type not registered in registry (infrastructure effect)
        var registry = new EffectRegistry();
        var factory = new EnabledStepEffectFactory();
        // Intentionally NOT registering the factory type

        // Act
        using var runner = new StepEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void StepEffectRunner_MixOfEnabledAndDisabled_OnlyCreatesEnabled()
    {
        // Arrange - use concrete stub types so GetType() returns distinct types
        var registry = new EffectRegistry();
        var enabledFactory = new EnabledStepEffectFactory();
        var disabledFactory = new DisabledStepEffectFactory();

        registry.Register(typeof(EnabledStepEffectFactory), enabled: true);
        registry.Register(typeof(DisabledStepEffectFactory), enabled: false);

        // Act
        using var runner = new StepEffectRunner([enabledFactory, disabledFactory], registry);

        // Assert
        enabledFactory.CreateCalled.Should().BeTrue();
        disabledFactory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void StepEffectRunner_NonToggleableFactory_CannotBeDisabled()
    {
        // Arrange - register as non-toggleable, then try to disable
        var registry = new EffectRegistry();
        var factory = new EnabledStepEffectFactory();
        registry.Register(typeof(EnabledStepEffectFactory), enabled: true, toggleable: false);

        // Attempt to disable should be a no-op
        registry.Disable(typeof(EnabledStepEffectFactory));

        // Act
        using var runner = new StepEffectRunner([factory], registry);

        // Assert - factory should still run because it can't be disabled
        factory.CreateCalled.Should().BeTrue();
    }

    #endregion

    #region Test Stubs

    private class StubEffectProvider : IEffectProvider
    {
        public Task SaveChanges(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Track(IModel model) => Task.CompletedTask;

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose() { }
    }

    private class EnabledEffectFactory : IEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IEffectProvider Create()
        {
            CreateCalled = true;
            return new StubEffectProvider();
        }
    }

    private class DisabledEffectFactory : IEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IEffectProvider Create()
        {
            CreateCalled = true;
            return new StubEffectProvider();
        }
    }

    private class StubStepEffectProvider : IStepEffectProvider
    {
        public Task BeforeStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task AfterStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public void Dispose() { }
    }

    private class EnabledStepEffectFactory : IStepEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IStepEffectProvider Create()
        {
            CreateCalled = true;
            return new StubStepEffectProvider();
        }
    }

    private class DisabledStepEffectFactory : IStepEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IStepEffectProvider Create()
        {
            CreateCalled = true;
            return new StubStepEffectProvider();
        }
    }

    #endregion
}
