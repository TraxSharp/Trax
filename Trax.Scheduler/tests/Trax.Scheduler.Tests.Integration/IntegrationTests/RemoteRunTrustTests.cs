using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A runner's run endpoint trusts the authorization of the train it was sent, which was checked
/// where the run was submitted, and nothing else: the train runs outside the trusted scope, so a
/// gated train it queues is checked as it would be on the scheduler's own host. This host
/// registers no <c>ITrainAuthorizationService</c>, so outside a trusted scope a gated train is
/// refused.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RemoteRunTrustTests : TestSetup
{
    private TraxRequestHandler Handler =>
        ActivatorUtilities.CreateInstance<TraxRequestHandler>(Scope.ServiceProvider, JobRunner);

    [Test]
    public async Task A_remote_run_of_a_gated_train_runs_it()
    {
        var response = await Handler.RunTrainAsync(
            new RemoteRunRequest(
                typeof(IGatedRemoteTrain).FullName!,
                "{}",
                typeof(GatedRemoteInput).FullName!
            )
        );

        response
            .IsError.Should()
            .BeFalse(
                "the named train was authorized where it was submitted, so the runner trusts its "
                    + "authorization (see docs/adr/0006-a-runner-requires-an-authorization-posture.md): "
                    + response.ErrorMessage
            );
    }

    [Test]
    public async Task A_remote_run_whose_junction_queues_a_gated_train_is_refused()
    {
        var response = await Handler.RunTrainAsync(
            new RemoteRunRequest(
                typeof(IQueuesGatedTrain).FullName!,
                "{}",
                typeof(QueuesGatedInput).FullName!
            )
        );

        response
            .IsError.Should()
            .BeTrue(
                "the train runs outside the trusted scope, so the gated train it queues is checked "
                    + "and refused (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );

        DataContext.Reset();
        (
            await DataContext
                .WorkQueues.AsNoTracking()
                .CountAsync(w => w.TrainName == typeof(IAdminOnlyTrain).FullName)
        )
            .Should()
            .Be(0, "the refused enqueue wrote nothing");
    }

    [Test]
    public void A_runner_on_the_mediators_execution_service_does_not_warn()
    {
        var logger = new WarningLogger();

        TraxRequestHandler.WarnIfExecutionServiceReplaced(
            Scope.ServiceProvider,
            logger,
            "/trax/run"
        );

        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public void A_runner_on_a_replaced_execution_service_warns_that_gated_runs_are_refused()
    {
        var services = new ServiceCollection()
            .AddScoped(_ => NSubstitute.Substitute.For<ITrainExecutionService>())
            .BuildServiceProvider();
        var logger = new WarningLogger();

        TraxRequestHandler.WarnIfExecutionServiceReplaced(services, logger, "/trax/run");

        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("[TraxAuthorize]")
            .And.Contain("/trax/run");
    }

    private sealed class WarningLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Test]
    public async Task A_remote_runs_junctions_do_not_see_a_trusted_scope()
    {
        var key = Guid.NewGuid().ToString("N");

        var response = await Handler.RunTrainAsync(
            new RemoteRunRequest(
                typeof(IReadsTrustTrain).FullName!,
                $$"""{"key":"{{key}}"}""",
                typeof(ReadsTrustInput).FullName!
            )
        );

        response.IsError.Should().BeFalse(response.ErrorMessage);
        ReadTrust
            .Seen.Should()
            .ContainKey(key)
            .WhoseValue.Should()
            .BeFalse(
                "the trusted scope closes before the train runs (see "
                    + "docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }
}

public record GatedRemoteInput;

public interface IGatedRemoteTrain : IServiceTrain<GatedRemoteInput, Unit>;

[TraxAuthorize(Roles = "Admin")]
public class GatedRemoteTrain : ServiceTrain<GatedRemoteInput, Unit>, IGatedRemoteTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
}

public record AdminOnlyInput;

public interface IAdminOnlyTrain : IServiceTrain<AdminOnlyInput, Unit>;

[TraxAuthorize(Roles = "Admin")]
public class AdminOnlyTrain : ServiceTrain<AdminOnlyInput, Unit>, IAdminOnlyTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
}

public record QueuesGatedInput;

public interface IQueuesGatedTrain : IServiceTrain<QueuesGatedInput, Unit>;

public class QueuesGatedTrain : ServiceTrain<QueuesGatedInput, Unit>, IQueuesGatedTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<QueueAdminOnly>().Resolve();
}

public class QueueAdminOnly(ITrainExecutionService trains) : Junction<QueuesGatedInput, Unit>
{
    public override async Task<Unit> Run(QueuesGatedInput input)
    {
        await trains.QueueAsync(typeof(IAdminOnlyTrain).FullName!, "{}");
        return Unit.Default;
    }
}

public record ReadsTrustInput
{
    public string Key { get; init; } = string.Empty;
}

public interface IReadsTrustTrain : IServiceTrain<ReadsTrustInput, Unit>;

public class ReadsTrustTrain : ServiceTrain<ReadsTrustInput, Unit>, IReadsTrustTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Chain<ReadTrust>().Resolve();
}

public class ReadTrust(ITrustedExecutionScope trust) : Junction<ReadsTrustInput, Unit>
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, bool> Seen { get; } =
        new();

    public override Task<Unit> Run(ReadsTrustInput input)
    {
        Seen[input.Key] = trust.IsTrusted;
        return Task.FromResult(Unit.Default);
    }
}
