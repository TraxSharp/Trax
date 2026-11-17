using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task RunAsync_TrainWhoseOutputTypeIsNotPublic_ReturnsItsOutput()
    {
        var scope = _serviceProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () =>
            execution.RunAsync(
                typeof(INonPublicOutputTrain).FullName.NotNull(),
                """{"name":"x"}"""
            );

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Output.Should().BeOfType<NonPublicOutput>();
    }

    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
    )]
    internal sealed record NonPublicInput
    {
        public string Name { get; init; } = "";
    }

    internal sealed record NonPublicOutput(string Value);

    internal interface INonPublicOutputTrain : IServiceTrain<NonPublicInput, NonPublicOutput>;

    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
    )]
    internal sealed class NonPublicOutputTrain
        : ServiceTrain<NonPublicInput, NonPublicOutput>,
            INonPublicOutputTrain
    {
        protected override Task<Either<Exception, NonPublicOutput>> Junctions() =>
            Task.FromResult<Either<Exception, NonPublicOutput>>(new NonPublicOutput("done"));
    }
}
