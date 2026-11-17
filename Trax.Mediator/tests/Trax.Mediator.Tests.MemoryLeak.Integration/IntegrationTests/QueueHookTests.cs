using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Covers the queue-time hook: <c>OnQueue</c> fires inside
/// <see cref="ITrainExecutionService.QueueAsync"/> before the work queue row is written,
/// only for trains that override it, and never on the synchronous run path.
/// </summary>
[TestFixture]
public class QueueHookTests
{
    private ServiceProvider? _serviceProvider;
    private QueueHookProbe? _probe;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<QueueHookProbe>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(QueueHookTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
        _probe = _serviceProvider.NotNull().GetRequiredService<QueueHookProbe>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        TrainBus.ClearMethodCache();
    }

    private async Task<long> CountWorkQueueAsync()
    {
        var factory = _serviceProvider.NotNull().GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context.WorkQueues.LongCountAsync().ConfigureAwait(false);
    }

    private static string Serialize(object input) =>
        JsonSerializer.Serialize(input, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    [Test]
    public async Task QueueAsync_OverridingTrain_FiresOnQueueBeforeInsert_WithInputAndExternalId()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        (await CountWorkQueueAsync()).Should().Be(0);

        var result = await svc.QueueAsync(
            typeof(IShadowQueueTrain).FullName.NotNull(),
            Serialize(new ShadowInput("hi"))
        );

        // The hook ran exactly once, observed the input, and saw the ExternalId the
        // eventual run will execute under (the same one returned to the caller).
        _probe.NotNull().OnQueueCallCount.Should().Be(1);
        _probe.NotNull().LastInput.Should().Be("hi");
        _probe.NotNull().LastExternalId.Should().Be(result.ExternalId);

        // Ordering: at hook time no row existed yet; exactly one exists after the call.
        _probe.NotNull().RowsAtHookTime.Should().Be(0);
        (await CountWorkQueueAsync()).Should().Be(1);

        // [Inject]/constructor wiring worked: the train was actually resolved.
        _probe.NotNull().Constructed.Should().Contain("Shadow");
    }

    [Test]
    public async Task QueueAsync_NonOverridingTrain_InsertsRow_AndNeverResolvesTrain()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await svc.QueueAsync(
            typeof(IPlainQueueTrain).FullName.NotNull(),
            Serialize(new PlainInput("hi"))
        );

        result.ExternalId.Should().NotBeNullOrEmpty();
        (await CountWorkQueueAsync()).Should().Be(1);

        // The hook never fired and — critically — the gate skipped resolution entirely,
        // so the enqueue path is as light as before the hook existed.
        _probe.NotNull().OnQueueCallCount.Should().Be(0);
        _probe.NotNull().Constructed.Should().NotContain("Plain");
    }

    [Test]
    public async Task QueueAsync_ThrowingOnQueue_AbortsEnqueue_NoRowInserted()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await svc.QueueAsync(
                    typeof(IRejectingQueueTrain).FullName.NotNull(),
                    Serialize(new RejectInput("x"))
                )
                .ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*OnQueue rejected*");

        // The exception propagated before the insert: no row was written.
        (await CountWorkQueueAsync())
            .Should()
            .Be(0);
        _probe.NotNull().OnQueueCallCount.Should().Be(0, "the throwing hook records nothing");
    }

    [Test]
    public async Task RunAsync_DoesNotFireOnQueue()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await svc.RunAsync(
            typeof(IShadowQueueTrain).FullName.NotNull(),
            Serialize(new ShadowInput("hi"))
        );

        result.MetadataId.Should().BeGreaterThan(0);
        _probe
            .NotNull()
            .OnQueueCallCount.Should()
            .Be(0, "OnQueue must not fire on the synchronous run path");
    }

    [Test]
    public async Task QueueAsync_OverridingTrain_CalledTwice_UsesCachedGate()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        await svc.QueueAsync(
            typeof(IShadowQueueTrain).FullName.NotNull(),
            Serialize(new ShadowInput("a"))
        );
        await svc.QueueAsync(
            typeof(IShadowQueueTrain).FullName.NotNull(),
            Serialize(new ShadowInput("b"))
        );

        _probe.NotNull().OnQueueCallCount.Should().Be(2);
        _probe.NotNull().LastInput.Should().Be("b");
        (await CountWorkQueueAsync()).Should().Be(2);
    }

    [Test]
    public async Task QueueAsync_CallerCancelsWhileAHookIgnoresItsToken_FailsAndLogsTheHooksLateFault()
    {
        // Its own host, to see the warnings. The in-memory provider has no transactions, so there
        // is no nested enqueue for the abandoned hook to be refused.
        var warnings = new WarningRecorder();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(warnings));
        services.AddSingleton<QueueHookProbe>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(QueueHookTests).Assembly])
        );
        var provider = services.BuildServiceProvider();
        await using var providerDisposal = provider.ConfigureAwait(false);
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        LateFaultQueueTrain.Reset();

        using var cancel = new CancellationTokenSource();
        var enqueue = svc.QueueAsync(
            typeof(ILateFaultQueueTrain).FullName.NotNull(),
            Serialize(new LateFaultInput("x")),
            ct: cancel.Token
        );
        await LateFaultQueueTrain.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(15),
            CancellationToken.None
        );
        await cancel.CancelAsync();

        var act = async () =>
            await enqueue
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The caller has its answer. Now the hook, still running, fails.
        LateFaultQueueTrain.Gate.TrySetResult();

        var logged = await warnings.Logged.Task.WaitAsync(
            TimeSpan.FromSeconds(15),
            CancellationToken.None
        );
        logged
            .Message.Should()
            .Be(
                $"{typeof(ILateFaultQueueTrain).FullName}.OnQueue threw after its enqueue had "
                    + "already been cancelled by its caller.",
                "nothing else observes a hook the enqueue stopped waiting for"
            );
        logged
            .Exception.Should()
            .BeOfType<AggregateException>()
            .Which.InnerException.Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(LateFaultQueueTrain.Reason);
        (await CountAsync(provider)).Should().Be(0, "a cancelled enqueue writes no entry");
    }

    private static async Task<long> CountAsync(IServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context.WorkQueues.LongCountAsync().ConfigureAwait(false);
    }

    #region Test infrastructure

    public class QueueHookProbe(IDataContextProviderFactory factory)
    {
        public IList<string> Constructed { get; } = [];
        public int OnQueueCallCount { get; private set; }
        public string? LastExternalId { get; private set; }
        public string? LastInput { get; private set; }
        public long RowsAtHookTime { get; private set; } = -1;

        public void MarkConstructed(string name) => Constructed.Add(name);

        public async Task CaptureOnQueueAsync(string externalId, string? input)
        {
            var context = await factory
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            RowsAtHookTime = await context.WorkQueues.LongCountAsync().ConfigureAwait(false);
            OnQueueCallCount++;
            LastExternalId = externalId;
            LastInput = input;
        }
    }

    public record ShadowInput(string Value);

    public record PlainInput(string Value);

    public record RejectInput(string Value);

    public interface IShadowQueueTrain : IServiceTrain<ShadowInput, string>;

    public class ShadowQueueTrain : ServiceTrain<ShadowInput, string>, IShadowQueueTrain
    {
        private readonly QueueHookProbe _probe;

        public ShadowQueueTrain(QueueHookProbe probe)
        {
            _probe = probe;
            probe.MarkConstructed("Shadow");
        }

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult(Extract<ShadowInput, string>().Resolve());

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            _probe
                .NotNull()
                .CaptureOnQueueAsync(metadata.ExternalId, metadata.GetInput<ShadowInput>()?.Value);
    }

    public interface IPlainQueueTrain : IServiceTrain<PlainInput, string>;

    public class PlainQueueTrain : ServiceTrain<PlainInput, string>, IPlainQueueTrain
    {
        public PlainQueueTrain(QueueHookProbe probe) => probe.MarkConstructed("Plain");

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult(Extract<PlainInput, string>().Resolve());
    }

    public interface IRejectingQueueTrain : IServiceTrain<RejectInput, string>;

    public class RejectingQueueTrain : ServiceTrain<RejectInput, string>, IRejectingQueueTrain
    {
        public RejectingQueueTrain(QueueHookProbe probe) => probe.MarkConstructed("Reject");

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult(Extract<RejectInput, string>().Resolve());

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("OnQueue rejected the enqueue");
    }

    /// <summary>Hands the first warning that carries an exception to the test.</summary>
    private sealed class WarningRecorder : ILoggerProvider, ILogger
    {
        public TaskCompletionSource<(string Message, Exception? Exception)> Logged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning && exception is not null)
                Logged.TrySetResult((formatter(state, exception), exception));
        }

        public void Dispose() { }
    }

    public record LateFaultInput(string Value);

    public interface ILateFaultQueueTrain : IServiceTrain<LateFaultInput, string>;

    /// <summary>A hook that ignores its token and fails once the test lets it go on.</summary>
    public class LateFaultQueueTrain : ServiceTrain<LateFaultInput, string>, ILateFaultQueueTrain
    {
        public const string Reason = "the hook failed after its caller had gone";

        public static TaskCompletionSource Started { get; private set; } = new();

        public static TaskCompletionSource Gate { get; private set; } = new();

        public static void Reset()
        {
            Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult(Extract<LateFaultInput, string>().Resolve());

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Started.TrySetResult();

            // Deliberately ignores the token.
            await Gate.Task.ConfigureAwait(false);

            throw new InvalidOperationException(Reason);
        }
    }

    #endregion
}
