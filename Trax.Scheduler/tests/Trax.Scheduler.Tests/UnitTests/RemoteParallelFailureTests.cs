using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// A run on a remote worker whose <c>Parallel</c> step failed brings home what the worker
/// recorded: the step's failure, the first branch to fail and the class combined from every failed
/// branch, which the worker's classifier decided while it held each branch's real exception.
///
/// <para>Enforces Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md")]
[TestFixture]
public class RemoteParallelFailureTests
{
    [Test]
    public async Task A_parallel_failure_crosses_the_remote_response_with_its_branch_and_combined_class()
    {
        await using var worker = new ServiceCollection()
            .AddSingleton<IFailureClassifier, RemoteBranchClassifier>()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .AddScopedTraxRoute<IRemoteBranchesTrain, RemoteBranchesTrain>()
            .BuildServiceProvider();

        // The worker runs the train and catches its failure, as TraxRequestHandler does.
        BranchesFailedException failure;
        using (var scope = worker.CreateScope())
            failure = (
                await FluentActions
                    .Awaiting(() =>
                        scope.ServiceProvider.GetRequiredService<IRemoteBranchesTrain>().Run("in")
                    )
                    .Should()
                    .ThrowAsync<BranchesFailedException>()
            ).Which;

        var response = TraxRequestHandler.BuildErrorResponse(failure);
        var wire = JsonSerializer.Deserialize<RemoteRunResponse>(
            JsonSerializer.Serialize(response)
        )!;
        var rebuilt = HttpRunExecutor.BuildExceptionFromErrorResponse(wire);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRemoteBranchesTrain).FullName!,
                ExternalId = "remote",
                Input = null,
            }
        );
        metadata.AddException(rebuilt);

        metadata.FailureException.Should().Be(nameof(BranchesFailedException));
        metadata
            .FailureJunction.Should()
            .MatchRegex("^Parallel#0/(transient|permanent):RemoteBreak");
        metadata
            .FailureClass.Should()
            .Be(
                FailureClass.Permanent,
                "the worker classified each branch and one branch's failure is permanent"
            );
        metadata.FailureReason.Should().Contain("2 branches failed");
    }
}

internal sealed class RemoteBranchClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            TimeoutException => FailureClass.Transient,
            InvalidOperationException => FailureClass.Permanent,
            _ => null,
        };
}

public sealed record RemoteLeft(string Value);

public sealed record RemoteRight(string Value);

public class RemoteBreakTransient : EffectJunction<string, RemoteLeft>
{
    public override async Task<RemoteLeft> Run(string input)
    {
        await Task.Yield();
        throw new TimeoutException("left timed out");
    }
}

public class RemoteBreakPermanent : EffectJunction<string, RemoteRight>
{
    public override async Task<RemoteRight> Run(string input)
    {
        await Task.Yield();
        throw new InvalidOperationException("right broke");
    }
}

public class RemoteEcho : EffectJunction<string, string>
{
    public override Task<string> Run(string input) => Task.FromResult(input);
}

public class RemoteJoin : EffectJunction<(RemoteLeft, RemoteRight), Unit>
{
    public override Task<Unit> Run((RemoteLeft, RemoteRight) input) =>
        Task.FromResult(Unit.Default);
}

public interface IRemoteBranchesTrain : IServiceTrain<string, Unit>;

public class RemoteBranchesTrain : ServiceTrain<string, Unit>, IRemoteBranchesTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<RemoteEcho>()
            .Parallel(p =>
                p.Branch("transient", b => b.Chain<RemoteBreakTransient>())
                    .Branch("permanent", b => b.Chain<RemoteBreakPermanent>())
                    .OnFailure(BranchFailurePolicy.WaitForAll)
            )
            .Chain<RemoteJoin>()
            .Resolve();
}
