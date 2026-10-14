using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

[TestFixture]
public class TrainEventReceiverServiceTests
{
    private ITrainEventReceiver _receiver = null!;
    private ITrainEventHandler _handler = null!;
    private ServiceProvider _serviceProvider = null!;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [SetUp]
    public void SetUp()
    {
        _receiver = Substitute.For<ITrainEventReceiver>();
        _handler = Substitute.For<ITrainEventHandler>();

        var services = new ServiceCollection();
        services.AddTransient<ITrainEventHandler>(_ => _handler);
        services.AddLogging();
        services.AddSingleton<BroadcastInstance>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        _serviceProvider?.Dispose();
        if (_receiver is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    private TrainEventReceiverService CreateService() =>
        new(
            _receiver,
            _serviceProvider,
            _serviceProvider.GetService<ILogger<TrainEventReceiverService>>()
        );

    // ExecuteAsync runs on the thread pool, so the service has started the receiver only once this completes.
    private static Task<Func<TrainLifecycleEventMessage, CancellationToken, Task>> Started(
        ITrainEventReceiver receiver
    )
    {
        var started = new TaskCompletionSource<
            Func<TrainLifecycleEventMessage, CancellationToken, Task>
        >(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver
            .StartAsync(
                Arg.Do<Func<TrainLifecycleEventMessage, CancellationToken, Task>>(h =>
                    started.TrySetResult(h)
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        return started.Task.WaitAsync(Timeout);
    }

    private static TrainLifecycleEventMessage CreateMessage(
        string eventType = "Completed",
        string? executor = "RemoteWorker"
    ) =>
        new(
            MetadataId: 1,
            ExternalId: "abc123",
            TrainName: "TestTrain",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: executor,
            Output: null
        );

    [Test]
    public async Task StartAsync_StartsReceiver()
    {
        var started = Started(_receiver);
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        await started;
        await service.StopAsync(cts.Token);

        await _receiver
            .Received(1)
            .StartAsync(
                Arg.Any<Func<TrainLifecycleEventMessage, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task DispatchesRemoteEventsToHandlers()
    {
        var started = Started(_receiver);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        var capturedHandler = await started;

        var message = CreateMessage(executor: "RemoteWorker");
        await capturedHandler(message, CancellationToken.None);

        await _handler
            .Received(1)
            .HandleAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.TrainName == "TestTrain" && m.EventType == "Completed"
                ),
                Arg.Any<CancellationToken>()
            );

        await service.StopAsync(cts.Token);
    }

    [Test]
    public async Task SkipsLocalEvents()
    {
        var started = Started(_receiver);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        var capturedHandler = await started;

        // Stamped with this host's instance id, as its own hook and sink stamp what they publish.
        var message = CreateMessage() with
        {
            InstanceId = _serviceProvider.GetRequiredService<BroadcastInstance>().Id,
        };
        await capturedHandler(message, CancellationToken.None);

        await _handler
            .DidNotReceive()
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());

        await service.StopAsync(cts.Token);
    }

    [Test]
    public async Task HandlerExceptionDoesNotCrashService()
    {
        var started = Started(_receiver);

        _handler
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler error")));

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        var capturedHandler = await started;

        var message = CreateMessage(executor: "RemoteWorker");

        // Should not throw despite handler failure
        var act = () => capturedHandler(message, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await service.StopAsync(cts.Token);
    }

    [Test]
    public async Task DispatchesToMultipleHandlers()
    {
        var handler1 = Substitute.For<ITrainEventHandler>();
        var handler2 = Substitute.For<ITrainEventHandler>();

        var services = new ServiceCollection();
        services.AddTransient<ITrainEventHandler>(_ => handler1);
        services.AddTransient<ITrainEventHandler>(_ => handler2);
        services.AddLogging();
        using var sp = services.BuildServiceProvider();

        var started = Started(_receiver);

        var service = new TrainEventReceiverService(_receiver, sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        var capturedHandler = await started;

        var message = CreateMessage(executor: "RemoteWorker");
        await capturedHandler(message, CancellationToken.None);

        await handler1
            .Received(1)
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());
        await handler2
            .Received(1)
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());

        await service.StopAsync(cts.Token);
    }

    [Test]
    public async Task StopAsync_CallsReceiverStop()
    {
        var started = Started(_receiver);
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        await started;
        await service.StopAsync(cts.Token);

        await _receiver.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectionFailure_RetriesInsteadOfCrashing()
    {
        var callCount = 0;

        _receiver
            .StartAsync(
                Arg.Any<Func<TrainLifecycleEventMessage, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                callCount++;
                throw new InvalidOperationException("Connection refused");
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await service.StartAsync(cts.Token);

        // The initial retry delay is 5s, so a second StartAsync invocation
        // should land within ~5-6s of the first. Poll for the actual second
        // call instead of sleeping a fixed window: the test then reflects the
        // service's real retry timing rather than racing GitHub Actions
        // scheduling overhead.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && callCount <= 1)
            // allowed-delay: polls for the retry's second StartAsync call.
            await Task.Delay(100);

        // Should not have crashed the host — service retries
        callCount.Should().BeGreaterThan(1, "the service should retry after connection failure");

        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ConnectionFailure_StopAsyncThrows_StillRetries()
    {
        // Covers the best-effort try/catch around _receiver.StopAsync in the retry path.
        var startCount = 0;

        _receiver
            .StartAsync(
                Arg.Any<Func<TrainLifecycleEventMessage, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                startCount++;
                throw new InvalidOperationException("Connection refused");
            });

        _receiver
            .StopAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("stop also broken")));

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await service.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && startCount <= 1)
            // allowed-delay: polls for the retry's second StartAsync call.
            await Task.Delay(100);

        startCount.Should().BeGreaterThan(1);

        // Reset the throwing StopAsync so the final teardown StopAsync does not re-throw.
        _receiver.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task EventWithNullExecutor_IsNotConsideredLocal()
    {
        var started = Started(_receiver);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StartAsync(cts.Token);
        var capturedHandler = await started;

        var message = CreateMessage(executor: null);
        await capturedHandler(message, CancellationToken.None);

        await _handler
            .Received(1)
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());

        await service.StopAsync(cts.Token);
    }
}
