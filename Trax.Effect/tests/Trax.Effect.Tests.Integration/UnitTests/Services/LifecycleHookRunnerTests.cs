using FluentAssertions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class LifecycleHookRunnerTests
{
    private EffectRegistry _registry;

    [SetUp]
    public void SetUp()
    {
        _registry = new EffectRegistry();
    }

    #region Construction & Filtering

    [Test]
    public void Constructor_EnabledFactory_CreatesHook()
    {
        var factory = new TrackingHookFactory();
        _registry.Register(typeof(TrackingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([factory], _registry);

        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void Constructor_DisabledFactory_SkipsCreate()
    {
        var factory = new TrackingHookFactory();
        _registry.Register(typeof(TrackingHookFactory), enabled: false);

        using var runner = new LifecycleHookRunner([factory], _registry);

        factory.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void Constructor_UntrackedFactory_CreatesHook()
    {
        var factory = new TrackingHookFactory();
        // Not registered in registry — infrastructure effects default to enabled

        using var runner = new LifecycleHookRunner([factory], _registry);

        factory.CreateCalled.Should().BeTrue();
    }

    [Test]
    public void Constructor_MixedFactories_OnlyCreatesEnabled()
    {
        var enabled = new TrackingHookFactory();
        var disabled = new DisabledTrackingHookFactory();

        _registry.Register(typeof(TrackingHookFactory), enabled: true);
        _registry.Register(typeof(DisabledTrackingHookFactory), enabled: false);

        using var runner = new LifecycleHookRunner([enabled, disabled], _registry);

        enabled.CreateCalled.Should().BeTrue();
        disabled.CreateCalled.Should().BeFalse();
    }

    [Test]
    public void Constructor_NoFactories_DoesNotThrow()
    {
        var act = () => new LifecycleHookRunner([], _registry);

        act.Should().NotThrow();
    }

    #endregion

    #region OnStarted

    [Test]
    public async Task OnStarted_BroadcastsToAllHooks()
    {
        var hook1 = new RecordingHook();
        var hook2 = new RecordingHook();
        var factory1 = new DirectHookFactory(hook1);
        var factory2 = new DirectHookFactory2(hook2);

        _registry.Register(typeof(DirectHookFactory), enabled: true);
        _registry.Register(typeof(DirectHookFactory2), enabled: true);

        using var runner = new LifecycleHookRunner([factory1, factory2], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnStarted(metadata, CancellationToken.None);

        hook1.StartedCalled.Should().BeTrue();
        hook2.StartedCalled.Should().BeTrue();
    }

    [Test]
    public async Task OnStarted_PassesMetadataToHook()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata("TestTrain");

        await runner.OnStarted(metadata, CancellationToken.None);

        hook.LastMetadata.Should().BeSameAs(metadata);
        hook.LastMetadata!.Name.Should().Be("TestTrain");
    }

    [Test]
    public async Task OnStarted_HookThrows_DoesNotPropagateException()
    {
        var throwingFactory = new ThrowingHookFactory();
        _registry.Register(typeof(ThrowingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory], _registry);
        var metadata = CreateTestMetadata();

        var act = () => runner.OnStarted(metadata, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnStarted_HookThrows_ContinuesToNextHook()
    {
        var throwingFactory = new ThrowingHookFactory();
        var hook = new RecordingHook();
        var recordingFactory = new DirectHookFactory2(hook);

        _registry.Register(typeof(ThrowingHookFactory), enabled: true);
        _registry.Register(typeof(DirectHookFactory2), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory, recordingFactory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnStarted(metadata, CancellationToken.None);

        hook.StartedCalled.Should().BeTrue();
    }

    #endregion

    #region OnCompleted

    [Test]
    public async Task OnCompleted_BroadcastsToAllHooks()
    {
        var hook1 = new RecordingHook();
        var hook2 = new RecordingHook();
        var factory1 = new DirectHookFactory(hook1);
        var factory2 = new DirectHookFactory2(hook2);

        _registry.Register(typeof(DirectHookFactory), enabled: true);
        _registry.Register(typeof(DirectHookFactory2), enabled: true);

        using var runner = new LifecycleHookRunner([factory1, factory2], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnCompleted(metadata, CancellationToken.None);

        hook1.CompletedCalled.Should().BeTrue();
        hook2.CompletedCalled.Should().BeTrue();
    }

    [Test]
    public async Task OnCompleted_HookThrows_DoesNotPropagate()
    {
        var throwingFactory = new ThrowingHookFactory();
        _registry.Register(typeof(ThrowingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory], _registry);

        var act = () => runner.OnCompleted(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region OnFailed

    [Test]
    public async Task OnFailed_BroadcastsToAllHooks()
    {
        var hook1 = new RecordingHook();
        var hook2 = new RecordingHook();
        var factory1 = new DirectHookFactory(hook1);
        var factory2 = new DirectHookFactory2(hook2);

        _registry.Register(typeof(DirectHookFactory), enabled: true);
        _registry.Register(typeof(DirectHookFactory2), enabled: true);

        using var runner = new LifecycleHookRunner([factory1, factory2], _registry);
        var metadata = CreateTestMetadata();
        var exception = new InvalidOperationException("test failure");

        await runner.OnFailed(metadata, exception, CancellationToken.None);

        hook1.FailedCalled.Should().BeTrue();
        hook1.LastException.Should().BeSameAs(exception);
        hook2.FailedCalled.Should().BeTrue();
    }

    [Test]
    public async Task OnFailed_HookThrows_DoesNotPropagate()
    {
        var throwingFactory = new ThrowingHookFactory();
        _registry.Register(typeof(ThrowingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory], _registry);

        var act = () =>
            runner.OnFailed(
                CreateTestMetadata(),
                new Exception("train failure"),
                CancellationToken.None
            );

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region OnCancelled

    [Test]
    public async Task OnCancelled_BroadcastsToAllHooks()
    {
        var hook1 = new RecordingHook();
        var hook2 = new RecordingHook();
        var factory1 = new DirectHookFactory(hook1);
        var factory2 = new DirectHookFactory2(hook2);

        _registry.Register(typeof(DirectHookFactory), enabled: true);
        _registry.Register(typeof(DirectHookFactory2), enabled: true);

        using var runner = new LifecycleHookRunner([factory1, factory2], _registry);

        await runner.OnCancelled(CreateTestMetadata(), CancellationToken.None);

        hook1.CancelledCalled.Should().BeTrue();
        hook2.CancelledCalled.Should().BeTrue();
    }

    [Test]
    public async Task OnCancelled_HookThrows_DoesNotPropagate()
    {
        var throwingFactory = new ThrowingHookFactory();
        _registry.Register(typeof(ThrowingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory], _registry);

        var act = () => runner.OnCancelled(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnCancelled_PassesCancellationToken()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await runner.OnCancelled(CreateTestMetadata(), token);

        hook.LastCancellationToken.Should().Be(token);
    }

    #endregion

    #region OnStateChanged — Automatic Invocation

    [Test]
    public async Task OnStarted_AlsoCallsOnStateChanged()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnStarted(metadata, CancellationToken.None);

        hook.StateChangedCount.Should().Be(1);
    }

    [Test]
    public async Task OnCompleted_AlsoCallsOnStateChanged()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnCompleted(metadata, CancellationToken.None);

        hook.StateChangedCount.Should().Be(1);
    }

    [Test]
    public async Task OnFailed_AlsoCallsOnStateChanged()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnFailed(metadata, new Exception("fail"), CancellationToken.None);

        hook.StateChangedCount.Should().Be(1);
    }

    [Test]
    public async Task OnCancelled_AlsoCallsOnStateChanged()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnCancelled(metadata, CancellationToken.None);

        hook.StateChangedCount.Should().Be(1);
    }

    [Test]
    public async Task OnStateChanged_NoHooks_DoesNotThrow()
    {
        using var runner = new LifecycleHookRunner([], _registry);

        var act = () => runner.OnStateChanged(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnStateChanged_HookThrows_DoesNotPropagate()
    {
        var throwingFactory = new ThrowingHookFactory();
        _registry.Register(typeof(ThrowingHookFactory), enabled: true);

        using var runner = new LifecycleHookRunner([throwingFactory], _registry);

        var act = () => runner.OnStateChanged(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task AllEvents_EachCallsOnStateChangedOnce()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        await runner.OnStarted(metadata, CancellationToken.None);
        await runner.OnCompleted(metadata, CancellationToken.None);
        await runner.OnFailed(metadata, new Exception(), CancellationToken.None);
        await runner.OnCancelled(metadata, CancellationToken.None);

        hook.StateChangedCount.Should().Be(4);
    }

    #endregion

    #region Dispose

    [Test]
    public void Dispose_DisposesDisposableHooks()
    {
        var hook = new DisposableRecordingHook();
        var factory = new DisposableHookFactory(hook);

        var runner = new LifecycleHookRunner([factory], _registry);
        runner.Dispose();

        hook.Disposed.Should().BeTrue();
    }

    [Test]
    public void Dispose_NonDisposableHook_DoesNotThrow()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        var runner = new LifecycleHookRunner([factory], _registry);
        var act = () => runner.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_HookDisposalThrows_DoesNotPropagate()
    {
        var hook = new ThrowingDisposableHook();
        var factory = new ThrowingDisposableHookFactory(hook);

        var runner = new LifecycleHookRunner([factory], _registry);
        var act = () => runner.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task Dispose_ClearsHooks_SubsequentCallsDoNotBroadcast()
    {
        var hook = new RecordingHook();
        var factory = new DirectHookFactory(hook);

        var runner = new LifecycleHookRunner([factory], _registry);
        runner.Dispose();

        // After dispose, hooks list is cleared — no broadcasts should happen
        await runner.OnStarted(CreateTestMetadata(), CancellationToken.None);

        hook.StartedCalled.Should().BeFalse();
    }

    #endregion

    #region No Hooks Registered

    [Test]
    public async Task OnStarted_NoHooks_DoesNotThrow()
    {
        using var runner = new LifecycleHookRunner([], _registry);

        var act = () => runner.OnStarted(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnCompleted_NoHooks_DoesNotThrow()
    {
        using var runner = new LifecycleHookRunner([], _registry);

        var act = () => runner.OnCompleted(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnFailed_NoHooks_DoesNotThrow()
    {
        using var runner = new LifecycleHookRunner([], _registry);

        var act = () =>
            runner.OnFailed(CreateTestMetadata(), new Exception("fail"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnCancelled_NoHooks_DoesNotThrow()
    {
        using var runner = new LifecycleHookRunner([], _registry);

        var act = () => runner.OnCancelled(CreateTestMetadata(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Default Interface Method

    [Test]
    public async Task DefaultInterfaceMethod_OnlyOverriddenMethodsCalled()
    {
        var hook = new PartialHook();
        var factory = new PartialHookFactory(hook);

        using var runner = new LifecycleHookRunner([factory], _registry);
        var metadata = CreateTestMetadata();

        // PartialHook only overrides OnCompleted — others use default no-op
        await runner.OnStarted(metadata, CancellationToken.None);
        await runner.OnCompleted(metadata, CancellationToken.None);
        await runner.OnFailed(metadata, new Exception(), CancellationToken.None);
        await runner.OnCancelled(metadata, CancellationToken.None);

        hook.CompletedCalled.Should().BeTrue();
    }

    #endregion

    #region Test Helpers

    private static Metadata CreateTestMetadata(string name = "Test.Train")
    {
        return new Metadata { Name = name, ExternalId = Guid.NewGuid().ToString("N") };
    }

    private class RecordingHook : ITrainLifecycleHook
    {
        public bool StartedCalled { get; private set; }
        public bool CompletedCalled { get; private set; }
        public bool FailedCalled { get; private set; }
        public bool CancelledCalled { get; private set; }
        public int StateChangedCount { get; private set; }
        public Metadata? LastMetadata { get; private set; }
        public Exception? LastException { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task OnStarted(Metadata metadata, CancellationToken ct)
        {
            StartedCalled = true;
            LastMetadata = metadata;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            LastMetadata = metadata;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
        {
            FailedCalled = true;
            LastMetadata = metadata;
            LastException = exception;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task OnCancelled(Metadata metadata, CancellationToken ct)
        {
            CancelledCalled = true;
            LastMetadata = metadata;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task OnStateChanged(Metadata metadata, CancellationToken ct)
        {
            StateChangedCount++;
            LastMetadata = metadata;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }
    }

    private class DisposableRecordingHook : ITrainLifecycleHook, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Task.CompletedTask;

        public Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }

    private class ThrowingDisposableHook : ITrainLifecycleHook, IDisposable
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Task.CompletedTask;

        public Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

        public void Dispose() => throw new InvalidOperationException("dispose failed");
    }

    private class ThrowingHook : ITrainLifecycleHook
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("hook failed");

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("hook failed");

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            throw new InvalidOperationException("hook failed");

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("hook failed");
    }

    /// <summary>
    /// Hook that only overrides OnCompleted — all other methods use default no-op.
    /// </summary>
    private class PartialHook : ITrainLifecycleHook
    {
        public bool CompletedCalled { get; private set; }

        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            return Task.CompletedTask;
        }
    }

    private class TrackingHookFactory : ITrainLifecycleHookFactory
    {
        public bool CreateCalled { get; private set; }

        public ITrainLifecycleHook Create()
        {
            CreateCalled = true;
            return new RecordingHook();
        }
    }

    private class DisabledTrackingHookFactory : ITrainLifecycleHookFactory
    {
        public bool CreateCalled { get; private set; }

        public ITrainLifecycleHook Create()
        {
            CreateCalled = true;
            return new RecordingHook();
        }
    }

    private class DirectHookFactory(ITrainLifecycleHook hook) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class DirectHookFactory2(ITrainLifecycleHook hook) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class ThrowingHookFactory : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => new ThrowingHook();
    }

    private class DisposableHookFactory(DisposableRecordingHook hook) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class ThrowingDisposableHookFactory(ThrowingDisposableHook hook)
        : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class PartialHookFactory(PartialHook hook) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    #endregion
}
