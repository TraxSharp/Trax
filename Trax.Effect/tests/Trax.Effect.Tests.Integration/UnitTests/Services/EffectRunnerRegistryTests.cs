using FluentAssertions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.ServiceTrain;

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
    public async Task EffectRunner_Track_AwaitsAsyncProviders()
    {
        var registry = new EffectRegistry();
        var factory = new AsyncTrackingEffectFactory();
        registry.Register(typeof(AsyncTrackingEffectFactory), enabled: true);

        using var runner = new EffectRunner([factory], registry);
        await runner.Track(new FakeModel());

        factory.Provider.TrackCompleted.Should().BeTrue();
    }

    [Test]
    public async Task EffectRunner_Update_AwaitsAsyncProviders()
    {
        var registry = new EffectRegistry();
        var factory = new AsyncTrackingEffectFactory();
        registry.Register(typeof(AsyncTrackingEffectFactory), enabled: true);

        using var runner = new EffectRunner([factory], registry);
        await runner.Update(new FakeModel());

        factory.Provider.UpdateCompleted.Should().BeTrue();
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

    #region EffectRunner with a throwing provider

    [Test]
    public async Task SaveChanges_first_provider_throws_the_second_still_saves_and_the_exception_surfaces()
    {
        var registry = new EffectRegistry();
        var throwing = new ThrowingEffectFactory(new InvalidOperationException("first"));
        var tracking = new TrackingEffectFactory();

        using var runner = new EffectRunner([throwing, tracking], registry);
        var act = () => runner.SaveChanges(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("first");
        tracking.Provider.SaveChangesCalled.Should().BeTrue();
    }

    [Test]
    public async Task Update_first_provider_throws_the_second_still_updates()
    {
        var registry = new EffectRegistry();
        var throwing = new ThrowingEffectFactory(new InvalidOperationException("first"));
        var tracking = new TrackingEffectFactory();

        using var runner = new EffectRunner([throwing, tracking], registry);
        var act = () => runner.Update(new FakeModel());

        await act.Should().ThrowAsync<InvalidOperationException>();
        tracking.Provider.UpdateCalled.Should().BeTrue();
    }

    [Test]
    public async Task Track_first_provider_throws_the_second_still_tracks()
    {
        var registry = new EffectRegistry();
        var throwing = new ThrowingEffectFactory(new InvalidOperationException("first"));
        var tracking = new TrackingEffectFactory();

        using var runner = new EffectRunner([throwing, tracking], registry);
        var act = () => runner.Track(new FakeModel());

        await act.Should().ThrowAsync<InvalidOperationException>();
        tracking.Provider.TrackCalled.Should().BeTrue();
    }

    [Test]
    public async Task SaveChanges_two_providers_throw_surfaces_both_in_an_AggregateException()
    {
        var registry = new EffectRegistry();
        var first = new ThrowingEffectFactory(new InvalidOperationException("first"));
        var second = new ThrowingEffectFactory2(new ArgumentException("second"));

        using var runner = new EffectRunner([first, second], registry);
        var act = () => runner.SaveChanges(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown
            .Which.InnerExceptions.Select(e => e.Message)
            .Should()
            .BeEquivalentTo(["first", "second"]);
    }

    [Test]
    public async Task SaveChanges_every_provider_cancelled_surfaces_a_cancellation_not_an_aggregate()
    {
        var registry = new EffectRegistry();
        var first = new ThrowingEffectFactory(new OperationCanceledException());
        var second = new ThrowingEffectFactory2(new OperationCanceledException());

        using var runner = new EffectRunner([first, second], registry);
        var act = () => runner.SaveChanges(CancellationToken.None);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    #endregion

    #region JunctionEffectRunner

    [Test]
    public void JunctionEffectRunner_DisabledFactory_SkipsCreateCall()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new DisabledJunctionEffectFactory();
        registry.Register(typeof(DisabledJunctionEffectFactory), enabled: false);

        // Act
        using var runner = new JunctionEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void JunctionEffectRunner_EnabledFactory_CallsCreate()
    {
        // Arrange
        var registry = new EffectRegistry();
        var factory = new EnabledJunctionEffectFactory();
        registry.Register(typeof(EnabledJunctionEffectFactory), enabled: true);

        // Act
        using var runner = new JunctionEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void JunctionEffectRunner_UntrackedFactory_CallsCreate()
    {
        // Arrange - factory type not registered in registry (infrastructure effect)
        var registry = new EffectRegistry();
        var factory = new EnabledJunctionEffectFactory();
        // Intentionally NOT registering the factory type

        // Act
        using var runner = new JunctionEffectRunner([factory], registry);

        // Assert
        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void JunctionEffectRunner_MixOfEnabledAndDisabled_OnlyCreatesEnabled()
    {
        // Arrange - use concrete stub types so GetType() returns distinct types
        var registry = new EffectRegistry();
        var enabledFactory = new EnabledJunctionEffectFactory();
        var disabledFactory = new DisabledJunctionEffectFactory();

        registry.Register(typeof(EnabledJunctionEffectFactory), enabled: true);
        registry.Register(typeof(DisabledJunctionEffectFactory), enabled: false);

        // Act
        using var runner = new JunctionEffectRunner([enabledFactory, disabledFactory], registry);

        // Assert
        enabledFactory.CreateCalled.Should().BeTrue();
        disabledFactory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void JunctionEffectRunner_NonToggleableFactory_CannotBeDisabled()
    {
        // Arrange - register as non-toggleable, then try to disable
        var registry = new EffectRegistry();
        var factory = new EnabledJunctionEffectFactory();
        registry.Register(typeof(EnabledJunctionEffectFactory), enabled: true, toggleable: false);

        // Attempt to disable should be a no-op
        registry.Disable(typeof(EnabledJunctionEffectFactory));

        // Act
        using var runner = new JunctionEffectRunner([factory], registry);

        // Assert - factory should still run because it can't be disabled
        factory.CreateCalled.Should().BeTrue();
    }

    #endregion

    #region JunctionEffectRunner Behavior

    [Test]
    public void JunctionEffectRunner_Dispose_HandlesProviderDisposalFailure()
    {
        var registry = new EffectRegistry();
        var throwingFactory = new ThrowingDisposeJunctionEffectFactory();
        var normalFactory = new EnabledJunctionEffectFactory();
        registry.Register(typeof(ThrowingDisposeJunctionEffectFactory), enabled: true);
        registry.Register(typeof(EnabledJunctionEffectFactory), enabled: true);

        var runner = new JunctionEffectRunner([throwingFactory, normalFactory], registry);

        var act = () => runner.Dispose();
        act.Should().NotThrow();

        throwingFactory.Provider.DisposeCalled.Should().BeTrue();
    }

    [Test]
    public void JunctionEffectRunner_Dispose_LogsHowManyProvidersItDisposed()
    {
        var registry = new EffectRegistry();
        var logger = new RecordingLogger<JunctionEffectRunner>();
        var runner = new JunctionEffectRunner(
            [new EnabledJunctionEffectFactory(), new DisabledJunctionEffectFactory()],
            registry,
            logger
        );

        runner.Dispose();

        logger.Messages.Should().ContainSingle(m => m.Contains("(2)"));
    }

    [Test]
    public void EffectRunner_Dispose_LogsHowManyProvidersItDisposed()
    {
        var registry = new EffectRegistry();
        var logger = new RecordingLogger<EffectRunner>();
        var runner = new EffectRunner(
            [new EnabledEffectFactory(), new DisabledEffectFactory()],
            registry,
            logger
        );

        runner.Dispose();

        logger.Messages.Should().ContainSingle(m => m.Contains("(2)"));
    }

    #endregion

    #region Test Stubs

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }

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

    private class StubJunctionEffectProvider : IJunctionEffectProvider
    {
        public Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectJunction<TIn, TOut> effectJunction,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectJunction<TIn, TOut> effectJunction,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public void Dispose() { }
    }

    private class EnabledJunctionEffectFactory : IJunctionEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IJunctionEffectProvider Create()
        {
            CreateCalled = true;
            return new StubJunctionEffectProvider();
        }
    }

    private class DisabledJunctionEffectFactory : IJunctionEffectProviderFactory
    {
        public bool CreateCalled { get; private set; }

        public IJunctionEffectProvider Create()
        {
            CreateCalled = true;
            return new StubJunctionEffectProvider();
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

    private class ThrowingEffectProvider(Exception exception) : IEffectProvider
    {
        public Task SaveChanges(CancellationToken cancellationToken) =>
            Task.FromException(exception);

        public Task Track(IModel model) => Task.FromException(exception);

        public Task Update(IModel model) => Task.FromException(exception);

        public void Dispose() { }
    }

    private class ThrowingEffectFactory(Exception exception) : IEffectProviderFactory
    {
        public IEffectProvider Create() => new ThrowingEffectProvider(exception);
    }

    private class ThrowingEffectFactory2(Exception exception) : IEffectProviderFactory
    {
        public IEffectProvider Create() => new ThrowingEffectProvider(exception);
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

    private class ThrowingDisposeJunctionEffectProvider : IJunctionEffectProvider
    {
        public bool DisposeCalled { get; private set; }

        public Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectJunction<TIn, TOut> effectJunction,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
            EffectJunction<TIn, TOut> effectJunction,
            ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public void Dispose()
        {
            DisposeCalled = true;
            throw new InvalidOperationException("Disposal failed");
        }
    }

    private class ThrowingDisposeJunctionEffectFactory : IJunctionEffectProviderFactory
    {
        public ThrowingDisposeJunctionEffectProvider Provider { get; } = new();

        public IJunctionEffectProvider Create() => Provider;
    }

    private class AsyncTrackingEffectProvider : IEffectProvider
    {
        public bool TrackCompleted { get; private set; }
        public bool UpdateCompleted { get; private set; }

        public Task SaveChanges(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task Track(IModel model)
        {
            await Task.Yield();
            TrackCompleted = true;
        }

        public async Task Update(IModel model)
        {
            await Task.Yield();
            UpdateCompleted = true;
        }

        public void Dispose() { }
    }

    private class AsyncTrackingEffectFactory : IEffectProviderFactory
    {
        public AsyncTrackingEffectProvider Provider { get; } = new();

        public IEffectProvider Create() => Provider;
    }

    #endregion
}
