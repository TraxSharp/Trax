using FluentAssertions;
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

    #region EffectRunner Behavior

    [Test]
    public async Task EffectRunner_SaveChanges_CallsAllProviders()
    {
        var registry = new EffectRegistry();
        var factory1 = new TrackingEffectFactory();
        var factory2 = new TrackingEffectFactory2();
        registry.Register(typeof(TrackingEffectFactory), enabled: true);
        registry.Register(typeof(TrackingEffectFactory2), enabled: true);

        using var runner = new EffectRunner([factory1, factory2], registry);
        await runner.SaveChanges(CancellationToken.None);

        factory1.Provider.SaveChangesCalled.Should().BeTrue();
        factory2.Provider.SaveChangesCalled.Should().BeTrue();
    }

    [Test]
    public async Task EffectRunner_Track_DelegatesToAllProviders()
    {
        var registry = new EffectRegistry();
        var factory1 = new TrackingEffectFactory();
        var factory2 = new TrackingEffectFactory2();
        registry.Register(typeof(TrackingEffectFactory), enabled: true);
        registry.Register(typeof(TrackingEffectFactory2), enabled: true);

        using var runner = new EffectRunner([factory1, factory2], registry);
        await runner.Track(new FakeModel());

        factory1.Provider.TrackCalled.Should().BeTrue();
        factory2.Provider.TrackCalled.Should().BeTrue();
    }

    [Test]
    public async Task EffectRunner_Update_DelegatesToAllProviders()
    {
        var registry = new EffectRegistry();
        var factory1 = new TrackingEffectFactory();
        var factory2 = new TrackingEffectFactory2();
        registry.Register(typeof(TrackingEffectFactory), enabled: true);
        registry.Register(typeof(TrackingEffectFactory2), enabled: true);

        using var runner = new EffectRunner([factory1, factory2], registry);
        await runner.Update(new FakeModel());

        factory1.Provider.UpdateCalled.Should().BeTrue();
        factory2.Provider.UpdateCalled.Should().BeTrue();
    }

    [Test]
    public void EffectRunner_Dispose_HandlesProviderDisposalFailure()
    {
        var registry = new EffectRegistry();
        var throwingFactory = new ThrowingDisposeEffectFactory();
        var normalFactory = new TrackingEffectFactory();
        registry.Register(typeof(ThrowingDisposeEffectFactory), enabled: true);
        registry.Register(typeof(TrackingEffectFactory), enabled: true);

        var runner = new EffectRunner([throwingFactory, normalFactory], registry);

        // Should not throw — disposal failures are caught internally
        var act = () => runner.Dispose();
        act.Should().NotThrow();

        // Both providers should have had Dispose called
        throwingFactory.Provider.DisposeCalled.Should().BeTrue();
        normalFactory.Provider.DisposeCalled.Should().BeTrue();
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

    #region StepEffectRunner Behavior

    [Test]
    public void StepEffectRunner_Dispose_HandlesProviderDisposalFailure()
    {
        var registry = new EffectRegistry();
        var throwingFactory = new ThrowingDisposeStepEffectFactory();
        var normalFactory = new EnabledStepEffectFactory();
        registry.Register(typeof(ThrowingDisposeStepEffectFactory), enabled: true);
        registry.Register(typeof(EnabledStepEffectFactory), enabled: true);

        var runner = new StepEffectRunner([throwingFactory, normalFactory], registry);

        var act = () => runner.Dispose();
        act.Should().NotThrow();

        throwingFactory.Provider.DisposeCalled.Should().BeTrue();
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
        public Task BeforeStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task AfterStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
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

    private class FakeModel : IModel
    {
        public long Id => 1;

        public override string ToString() => "FakeModel";
    }

    private class TrackingEffectProvider : IEffectProvider
    {
        public bool SaveChangesCalled { get; private set; }
        public bool TrackCalled { get; private set; }
        public bool UpdateCalled { get; private set; }
        public bool DisposeCalled { get; private set; }

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task Track(IModel model)
        {
            TrackCalled = true;
            return Task.CompletedTask;
        }

        public Task Update(IModel model)
        {
            UpdateCalled = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCalled = true;
        }
    }

    private class TrackingEffectFactory : IEffectProviderFactory
    {
        public TrackingEffectProvider Provider { get; } = new();

        public IEffectProvider Create() => Provider;
    }

    private class TrackingEffectFactory2 : IEffectProviderFactory
    {
        public TrackingEffectProvider Provider { get; } = new();

        public IEffectProvider Create() => Provider;
    }

    private class ThrowingDisposeEffectProvider : IEffectProvider
    {
        public bool DisposeCalled { get; private set; }

        public Task SaveChanges(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Track(IModel model) => Task.CompletedTask;

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose()
        {
            DisposeCalled = true;
            throw new InvalidOperationException("Disposal failed");
        }
    }

    private class ThrowingDisposeEffectFactory : IEffectProviderFactory
    {
        public ThrowingDisposeEffectProvider Provider { get; } = new();

        public IEffectProvider Create() => Provider;
    }

    private class ThrowingDisposeStepEffectProvider : IStepEffectProvider
    {
        public bool DisposeCalled { get; private set; }

        public Task BeforeStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task AfterStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectStep<TIn, TOut> effectStep,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public void Dispose()
        {
            DisposeCalled = true;
            throw new InvalidOperationException("Disposal failed");
        }
    }

    private class ThrowingDisposeStepEffectFactory : IStepEffectProviderFactory
    {
        public ThrowingDisposeStepEffectProvider Provider { get; } = new();

        public IStepEffectProvider Create() => Provider;
    }

    #endregion
}
