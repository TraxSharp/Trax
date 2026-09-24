using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The operations surface enqueues through the real mediator, so a train's authorization
/// requirements apply to it. The unit tests substitute the mediator; these do not, so they pin
/// what a caller of the queueTrain mutation or the dashboard's queue dialog actually meets.
///
/// <para>Enforces Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class OperationsServiceAuthorizationTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

        _serviceProvider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<ITrainAuthorizationService, DenyingAuthorization>()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(connectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _serviceProvider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        await TestSetup.CleanupDatabase(_scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [TearDown]
    public void TestTearDown() => _scope.Dispose();

    private IOperationsService Operations =>
        _scope.ServiceProvider.GetRequiredService<IOperationsService>();

    [Test]
    public async Task A_caller_who_may_not_run_the_train_is_refused_and_nothing_is_queued()
    {
        var act = async () =>
            await Operations.QueueTrainAsync(
                new QueueTrainInput(typeof(IGuardedTrain).FullName!, "{\"Value\":\"x\"}"),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var context = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.WorkQueues.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task Malformed_input_from_a_caller_who_may_not_run_the_train_is_not_parsed()
    {
        var act = async () =>
            await Operations.QueueTrainAsync(
                new QueueTrainInput(typeof(IGuardedTrain).FullName!, "{not json"),
                CancellationToken.None
            );

        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>(
                "authorization comes first, so an unauthorized caller learns nothing about the "
                    + "input the train expects from a parse error"
            );
    }

    [Test]
    public async Task Malformed_input_from_a_caller_who_may_run_the_train_is_a_failed_result()
    {
        var result = await Operations.QueueTrainAsync(
            new QueueTrainInput(typeof(IOpenTrain).FullName!, "{not json"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("Invalid InputJson");
    }

    [Test]
    public async Task An_enqueue_the_train_refuses_is_a_failed_result_not_an_error()
    {
        var result = await Operations.QueueTrainAsync(
            new QueueTrainInput(typeof(IRefusingHookTrain).FullName!, "{}"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse("the train's own OnQueue refused the mutation");
        result.Message.Should().Contain("hook refused");

        var context = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.WorkQueues.CountAsync()).Should().Be(0);
    }

    public class DenyingAuthorization : ITrainAuthorizationService
    {
        public Task AuthorizeAsync(
            TrainRegistration registration,
            CancellationToken ct = default
        ) =>
            registration.HasAuthorizeAttribute
                ? throw new UnauthorizedAccessException("Not authorized.")
                : Task.CompletedTask;
    }

    public record GuardedInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record OpenInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IGuardedTrain : IServiceTrain<GuardedInput, Unit>;

    [TraxAuthorize(Roles = "Admin")]
    public class GuardedTrain : ServiceTrain<GuardedInput, Unit>, IGuardedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IOpenTrain : IServiceTrain<OpenInput, Unit>;

    public class OpenTrain : ServiceTrain<OpenInput, Unit>, IOpenTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record RefusingInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IRefusingHookTrain : IServiceTrain<RefusingInput, Unit>;

    public class RefusingHookTrain : ServiceTrain<RefusingInput, Unit>, IRefusingHookTrain
    {
        protected override Task OnQueue(
            Trax.Effect.Models.Metadata.Metadata metadata,
            CancellationToken ct
        ) => throw new InvalidOperationException("the hook refused this mutation");

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
