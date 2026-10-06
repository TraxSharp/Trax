using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task RunAsync_TrainWhoseOutputTypeIsNotPublic_ReturnsItsOutput()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () =>
            execution.RunAsync(typeof(INonPublicOutputTrain).FullName!, """{"name":"x"}""");

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Output.Should().BeOfType<NonPublicOutput>();
    }

    internal record NonPublicInput
    {
        public string Name { get; init; } = "";
    }

    internal record NonPublicOutput(string Value);

    internal interface INonPublicOutputTrain : IServiceTrain<NonPublicInput, NonPublicOutput>;

    internal class NonPublicOutputTrain
        : ServiceTrain<NonPublicInput, NonPublicOutput>,
            INonPublicOutputTrain
    {
        protected override Task<Either<Exception, NonPublicOutput>> Junctions() =>
            Task.FromResult<Either<Exception, NonPublicOutput>>(new NonPublicOutput("done"));
    }
}
