using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// What an enqueue reports when something on the way to the queue row throws.
///
/// <para>Two ways the real cause is lost. A train's own member reached by reflection throws
/// inside a <c>TargetInvocationException</c>, whose message names only "the target of an
/// invocation"; and the rollback that follows a failed commit can throw in its own right,
/// replacing the failure it was cleaning up after. Both surface through
/// <c>OperationsService</c> to a GraphQL client, so the wrong exception is the one an operator
/// reads.</para>
/// </summary>
[TestFixture]
public class TrainExecutionServiceEnqueueFailureTests
{
    public record EnqueueFailureInput(string Value);

    private const string InputJson = """{"Value":"probe"}""";

    /// <summary>
    /// Builds the service over substituted infrastructure. <paramref name="configureContext"/>
    /// shapes the data context the enqueue writes through; leaving it null gives one whose
    /// writes succeed.
    /// </summary>
    private static (TrainExecutionService Service, TrainRegistration Registration) Build<
        TService,
        TTrain
    >(Action<IDataContext>? configureContext = null, RecordingLogger? logger = null)
        where TService : class
        where TTrain : class, TService
    {
        var registration = Registration<TService, TTrain>();

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([registration]);

        var dataContext = Substitute.For<IDataContext>();
        configureContext?.Invoke(dataContext);

        var factory = Substitute.For<IDataContextProviderFactory>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(dataContext);

        var services = new ServiceCollection();
        services.AddScoped<TService, TTrain>();
        services.AddSingleton(Substitute.For<IEnqueueContextAccessor>());

        if (logger is not null)
            services.AddSingleton<ILogger<TrainExecutionService>>(logger);

        return (
            new TrainExecutionService(
                discovery,
                Substitute.For<IRunExecutor>(),
                Substitute.For<IConcurrencyLimiter>(),
                factory,
                new MediatorConfiguration(),
                services.BuildServiceProvider()
            ),
            registration
        );
    }

    private static TrainRegistration Registration<TService, TTrain>() =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TTrain),
            InputType = typeof(EnqueueFailureInput),
            OutputType = typeof(bool),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TTrain).Name,
            InputTypeName = nameof(EnqueueFailureInput),
            OutputTypeName = nameof(Boolean),
            HasAllowAnonymousAttribute = true,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = 0,
        };

    // ────────────────────────────────────────────────────────────────
    // A train member reached by reflection
    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Enqueue_WhenDeferQueuePromotionThrows_ReportsTheTrainsOwnException()
    {
        var (service, registration) = Build<IThrowingDeferTrain, ThrowingDeferTrain>();

        var enqueue = async () =>
            await service.QueueAsync(registration.ServiceType.FullName!, InputJson);

        (await enqueue.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            ThrowingDeferTrain.Reason,
            "QueueSubjectKey and OnQueue already unwrap the reflection wrapper; a caller "
                + "cannot act on \"Exception has been thrown by the target of an invocation.\""
        );
    }

    // ────────────────────────────────────────────────────────────────
    // A rollback that throws in its own right
    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Enqueue_WhenTheRollbackAlsoThrows_ReportsWhyTheEnqueueFailed()
    {
        var saveFailure = new InvalidOperationException("the write failed");
        var rollbackFailure = new InvalidOperationException("the connection was already gone");

        var transaction = Substitute.For<IDataContextTransaction>();
        transaction.Rollback().Returns(Task.FromException(rollbackFailure));

        var (service, registration) = Build<IHookedTrain, HookedTrain>(context =>
        {
            context.BeginTransaction(Arg.Any<CancellationToken>()).Returns(transaction);
            context
                .SaveChanges(Arg.Any<CancellationToken>())
                .Returns(Task.FromException(saveFailure));
        });

        var enqueue = async () =>
            await service.QueueAsync(registration.ServiceType.FullName!, InputJson);

        var thrown = (await enqueue.Should().ThrowAsync<InvalidOperationException>()).Which;

        thrown
            .Should()
            .BeSameAs(
                saveFailure,
                "the deferred path already guards its cleanup so a failure to undo does not hide "
                    + "why the enqueue failed, and the two paths should answer the same way"
            );
        thrown
            .Message.Should()
            .NotContain(
                rollbackFailure.Message,
                "the rollback's own failure is a consequence of the first one, not the cause"
            );
    }

    [Test]
    public async Task Enqueue_WhenTheRollbackAlsoThrows_StillDisposesTheTransaction()
    {
        var transaction = Substitute.For<IDataContextTransaction>();
        transaction
            .Rollback()
            .Returns(Task.FromException(new InvalidOperationException("rollback failed")));

        var (service, registration) = Build<IHookedTrain, HookedTrain>(context =>
        {
            context.BeginTransaction(Arg.Any<CancellationToken>()).Returns(transaction);
            context
                .SaveChanges(Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("the write failed")));
        });

        var enqueue = async () =>
            await service.QueueAsync(registration.ServiceType.FullName!, InputJson);

        await enqueue.Should().ThrowAsync<InvalidOperationException>();

        transaction.Received(1).Dispose();
    }

    [Test]
    public async Task Enqueue_WhenTheRollbackAlsoThrows_LogsWhyTheRollbackFailed()
    {
        var rollbackFailure = new InvalidOperationException("the connection was already gone");

        var transaction = Substitute.For<IDataContextTransaction>();
        transaction.Rollback().Returns(Task.FromException(rollbackFailure));

        var logger = new RecordingLogger();

        var (service, registration) = Build<IHookedTrain, HookedTrain>(
            context =>
            {
                context.BeginTransaction(Arg.Any<CancellationToken>()).Returns(transaction);
                context
                    .SaveChanges(Arg.Any<CancellationToken>())
                    .Returns(Task.FromException(new InvalidOperationException("the write failed")));
            },
            logger
        );

        var enqueue = async () =>
            await service.QueueAsync(registration.ServiceType.FullName!, InputJson);

        await enqueue.Should().ThrowAsync<InvalidOperationException>();

        logger
            .Warnings.Should()
            .ContainSingle(
                "a rollback that failed is not dropped on the floor: it usually means the "
                    + "transaction went with its connection and the entry may be left behind"
            )
            .Which.Exception.Should()
            .BeSameAs(rollbackFailure);
    }

    /// <summary>
    /// Keeps what the service logged, so a failure that was deliberately logged can be told from one
    /// that was swallowed.
    /// </summary>
    private sealed class RecordingLogger : ILogger<TrainExecutionService>
    {
        public List<(string Message, Exception? Exception)> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add((formatter(state, exception), exception));
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Trains
    // ────────────────────────────────────────────────────────────────

    private class ProbeToFlag : Junction<EnqueueFailureInput, bool>
    {
        public override Task<bool> Run(EnqueueFailureInput input) =>
            Task.FromResult(input.Value.Length > 0);
    }

    public interface IThrowingDeferTrain : IServiceTrain<EnqueueFailureInput, bool>;

    /// <summary>
    /// Overrides <c>OnQueue</c> so the defer property is read at all, then throws from the
    /// property itself.
    /// </summary>
    public class ThrowingDeferTrain : ServiceTrain<EnqueueFailureInput, bool>, IThrowingDeferTrain
    {
        public const string Reason = "the defer decision needs a tenant this train cannot see";

        protected override bool DeferQueuePromotion => throw new InvalidOperationException(Reason);

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ProbeToFlag>().Resolve();
    }

    public interface IHookedTrain : IServiceTrain<EnqueueFailureInput, bool>;

    /// <summary>Overrides <c>OnQueue</c> so the enqueue takes its transactional path.</summary>
    public class HookedTrain : ServiceTrain<EnqueueFailureInput, bool>, IHookedTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ProbeToFlag>().Resolve();
    }
}
