using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private ServiceProvider _serviceProvider = null!;
    private QueueHookProbe _probe = null!;

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
        _probe = _serviceProvider.GetRequiredService<QueueHookProbe>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    private async Task<long> CountWorkQueueAsync()
    {
        var factory = _serviceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.LongCountAsync();
    }

    private static string Serialize(object input) =>
        JsonSerializer.Serialize(input, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    [Test]
    public async Task QueueAsync_OverridingTrain_FiresOnQueueBeforeInsert_WithInputAndExternalId()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        (await CountWorkQueueAsync()).Should().Be(0);

        var result = await svc.QueueAsync(
            typeof(IShadowQueueTrain).FullName!,
            Serialize(new ShadowInput("hi"))
        );

        // The hook ran exactly once, observed the input, and saw the ExternalId the
        // eventual run will execute under (the same one returned to the caller).
        _probe.OnQueueCallCount.Should().Be(1);
        _probe.LastInput.Should().Be("hi");
        _probe.LastExternalId.Should().Be(result.ExternalId);

        // Ordering: at hook time no row existed yet; exactly one exists after the call.
        _probe.RowsAtHookTime.Should().Be(0);
        (await CountWorkQueueAsync()).Should().Be(1);

        // [Inject]/constructor wiring worked: the train was actually resolved.
        _probe.Constructed.Should().Contain("Shadow");
    }

    [Test]
    public async Task QueueAsync_NonOverridingTrain_InsertsRow_AndNeverResolvesTrain()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await svc.QueueAsync(
            typeof(IPlainQueueTrain).FullName!,
            Serialize(new PlainInput("hi"))
        );

        result.ExternalId.Should().NotBeNullOrEmpty();
        (await CountWorkQueueAsync()).Should().Be(1);

        // The hook never fired and — critically — the gate skipped resolution entirely,
        // so the enqueue path is as light as before the hook existed.
        _probe.OnQueueCallCount.Should().Be(0);
        _probe.Constructed.Should().NotContain("Plain");
    }

    [Test]
    public async Task QueueAsync_ThrowingOnQueue_AbortsEnqueue_NoRowInserted()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await svc.QueueAsync(
                typeof(IRejectingQueueTrain).FullName!,
                Serialize(new RejectInput("x"))
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*OnQueue rejected*");

        // The exception propagated before the insert: no row was written.
        (await CountWorkQueueAsync())
            .Should()
            .Be(0);
        _probe.OnQueueCallCount.Should().Be(0, "the throwing hook records nothing");
    }

    [Test]
    public async Task RunAsync_DoesNotFireOnQueue()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await svc.RunAsync(
            typeof(IShadowQueueTrain).FullName!,
            Serialize(new ShadowInput("hi"))
        );

        result.MetadataId.Should().BeGreaterThan(0);
        _probe.OnQueueCallCount.Should().Be(0, "OnQueue must not fire on the synchronous run path");
    }

    [Test]
    public async Task QueueAsync_OverridingTrain_CalledTwice_UsesCachedGate()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        await svc.QueueAsync(typeof(IShadowQueueTrain).FullName!, Serialize(new ShadowInput("a")));
        await svc.QueueAsync(typeof(IShadowQueueTrain).FullName!, Serialize(new ShadowInput("b")));

        _probe.OnQueueCallCount.Should().Be(2);
        _probe.LastInput.Should().Be("b");
        (await CountWorkQueueAsync()).Should().Be(2);
    }

    #region Test infrastructure

    public class QueueHookProbe(IDataContextProviderFactory factory)
    {
        public List<string> Constructed { get; } = [];
        public int OnQueueCallCount { get; private set; }
        public string? LastExternalId { get; private set; }
        public string? LastInput { get; private set; }
        public long RowsAtHookTime { get; private set; } = -1;

        public void MarkConstructed(string name) => Constructed.Add(name);

        public async Task CaptureOnQueueAsync(string externalId, string? input)
        {
            using var context = await factory.CreateDbContextAsync(CancellationToken.None);
            RowsAtHookTime = await context.WorkQueues.LongCountAsync();
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

        protected override Task<Either<Exception, string>> RunInternal(ShadowInput input) =>
            Task.FromResult<Either<Exception, string>>(input.Value);

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            _probe.CaptureOnQueueAsync(
                metadata.ExternalId,
                metadata.GetInput<ShadowInput>()?.Value
            );
    }

    public interface IPlainQueueTrain : IServiceTrain<PlainInput, string>;

    public class PlainQueueTrain : ServiceTrain<PlainInput, string>, IPlainQueueTrain
    {
        public PlainQueueTrain(QueueHookProbe probe) => probe.MarkConstructed("Plain");

        protected override Task<Either<Exception, string>> RunInternal(PlainInput input) =>
            Task.FromResult<Either<Exception, string>>(input.Value);
    }

    public interface IRejectingQueueTrain : IServiceTrain<RejectInput, string>;

    public class RejectingQueueTrain : ServiceTrain<RejectInput, string>, IRejectingQueueTrain
    {
        public RejectingQueueTrain(QueueHookProbe probe) => probe.MarkConstructed("Reject");

        protected override Task<Either<Exception, string>> RunInternal(RejectInput input) =>
            Task.FromResult<Either<Exception, string>>(input.Value);

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("OnQueue rejected the enqueue");
    }

    #endregion
}
