using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Running a train now applies the per-record checks queueing applies: the train's
/// <c>OnQueue</c> hook runs on the run's input before anything is written, and a train that
/// declares <c>QueueSubjectKey</c> is run now only inside a trusted scope. The real mediator and
/// the real operations service, so these pin what a caller of either surface meets.
///
/// <para>Enforces <c>Trax.Docs/adr/0037-run-now-applies-the-same-per-record-checks-as-queueing.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0037-run-now-applies-the-same-per-record-checks-as-queueing.md")]
public class OperationsServiceRunQueueChecksTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        var connectionString = TestPostgres.ConnectionString;

        _serviceProvider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
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
        TenantScopedTrain.Seen.Clear();
    }

    [TearDown]
    public void TestTearDown() => _scope.Dispose();

    private IOperationsService Operations =>
        _scope.ServiceProvider.GetRequiredService<IOperationsService>();

    private Task<OperationResult> Run(Type train, string json) =>
        Operations.RunTrainAsync(new RunTrainInput(train.FullName!, json), CancellationToken.None);

    private async Task<int> RunsOf(Type train)
    {
        var context = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.Metadatas.CountAsync(m => m.Name == train.FullName);
    }

    [Test]
    public async Task A_run_the_trains_on_queue_hook_refuses_is_a_failed_result_and_writes_no_run()
    {
        var result = await Run(typeof(ITenantScopedTrain), "{\"tenant\":\"other\"}");

        result.Success.Should().BeFalse("the train's OnQueue refuses records of another tenant");
        result.Message.Should().Be("The run was refused: Record belongs to another tenant.");
        (await RunsOf(typeof(ITenantScopedTrain)))
            .Should()
            .Be(0, "a refused run writes no metadata row");
    }

    [Test]
    public async Task A_run_the_trains_on_queue_hook_accepts_runs_under_the_hooks_external_id()
    {
        var result = await Run(typeof(ITenantScopedTrain), "{\"tenant\":\"mine\"}");

        result.Success.Should().BeTrue(result.Message);

        var context = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        var run = await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == result.Id);
        var seen = TenantScopedTrain.Seen.Should().ContainSingle().Subject;
        seen.ExternalId.Should()
            .Be(run.ExternalId, "the hook correlates on the id the run executes under");
        seen.Tenant.Should().Be("mine", "the hook reads the run's input through TrainInput");
        seen.HadEnqueueContext.Should()
            .BeTrue("the hook's writes join the write of the run's metadata row");
    }

    [Test]
    public async Task A_run_the_trains_on_queue_hook_refuses_is_refused_inside_a_trusted_scope_too()
    {
        var trusted = _scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>();

        OperationResult result;
        using (trusted.BeginTrusted("tests.run-now"))
            result = await Run(typeof(ITenantScopedTrain), "{\"tenant\":\"other\"}");

        result.Success.Should().BeFalse("a trusted scope skips authorization, not the queue hooks");
        (await RunsOf(typeof(ITenantScopedTrain))).Should().Be(0);
    }

    [Test]
    public async Task A_subject_keyed_train_is_refused_outside_a_trusted_scope_with_a_message_to_queue_it()
    {
        var result = await Run(typeof(ISubjectKeyedTrain), "{\"subject\":\"order-1\"}");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Queue it instead");
        (await RunsOf(typeof(ISubjectKeyedTrain)))
            .Should()
            .Be(0, "a refused run writes no metadata row");
    }

    [Test]
    public async Task A_subject_keyed_train_runs_inside_a_trusted_scope()
    {
        var trusted = _scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>();

        OperationResult result;
        using (trusted.BeginTrusted("tests.run-now"))
            result = await Run(typeof(ISubjectKeyedTrain), "{\"subject\":\"order-1\"}");

        result.Success.Should().BeTrue(result.Message);
        (await RunsOf(typeof(ISubjectKeyedTrain))).Should().Be(1);
    }

    [Test]
    public async Task A_refusal_of_another_type_is_reported_with_a_fixed_message()
    {
        var result = await Run(typeof(IFailingHookTrain), "{}");

        result.Success.Should().BeFalse();
        result.Message.Should().Be("The run was refused.");
        result.Message.Should().NotContain("secret-host");
        (await RunsOf(typeof(IFailingHookTrain))).Should().Be(0);
    }

    public record TenantInput
    {
        public string Tenant { get; init; } = string.Empty;
    }

    public sealed record SeenByHook(string ExternalId, string Tenant, bool HadEnqueueContext);

    public interface ITenantScopedTrain : IServiceTrain<TenantInput, Unit>;

    public class TenantScopedTrain(IEnqueueContextAccessor enqueueContext)
        : ServiceTrain<TenantInput, Unit>,
            ITenantScopedTrain
    {
        public static readonly List<SeenByHook> Seen = [];

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            if (TrainInput.Tenant != "mine")
                throw new TrainException("Record belongs to another tenant.");

            Seen.Add(
                new SeenByHook(
                    metadata.ExternalId,
                    TrainInput.Tenant,
                    enqueueContext.Current is not null
                )
            );
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record SubjectInput
    {
        public string Subject { get; init; } = string.Empty;
    }

    public interface ISubjectKeyedTrain : IServiceTrain<SubjectInput, Unit>;

    public class SubjectKeyedTrain : ServiceTrain<SubjectInput, Unit>, ISubjectKeyedTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata) => TrainInput.Subject;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record FailingHookInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IFailingHookTrain : IServiceTrain<FailingHookInput, Unit>;

    public class FailingHookTrain : ServiceTrain<FailingHookInput, Unit>, IFailingHookTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("secret-host:6379 did not answer");

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
