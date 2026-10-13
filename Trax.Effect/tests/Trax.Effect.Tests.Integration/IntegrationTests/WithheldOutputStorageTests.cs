using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A train marked <c>[OutputWithheldFromLifecycleHooks]</c> has no output stored by the parameter effect: its row is
/// kept with its input and without its output, whatever the configuration says. A train without the mark keeps its
/// output stored.
/// </summary>
public class WithheldOutputStorageTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
        );
        services
            .AddScopedTraxRoute<IWithheldTrain, WithheldTrain>()
            .AddScopedTraxRoute<IPlainTrain, PlainTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_withheld_output_is_not_stored_and_the_input_still_is()
    {
        using var scope = _provider.CreateScope();
        var train = (WithheldTrain)scope.ServiceProvider.GetRequiredService<IWithheldTrain>();

        var output = await train.Run("given");

        output.Should().Be("context-of-given", "the caller still gets the output");
        train.Metadata!.Output.Should().BeNull("the train withholds its output from every copy");
        train.Metadata.Input.Should().Contain("given", "the run's row keeps its input for audit");
    }

    [Test]
    public async Task A_plain_output_is_still_stored()
    {
        using var scope = _provider.CreateScope();
        var train = (PlainTrain)scope.ServiceProvider.GetRequiredService<IPlainTrain>();

        await train.Run("given");

        train.Metadata!.Output.Should().Contain("context-of-given");
    }

    public interface IWithheldTrain : IServiceTrain<string, string> { }

    [OutputWithheldFromLifecycleHooks]
    private class WithheldTrain : ServiceTrain<string, string>, IWithheldTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult<Either<Exception, string>>($"context-of-{TrainInput}");
    }

    public interface IPlainTrain : IServiceTrain<string, string> { }

    private class PlainTrain : ServiceTrain<string, string>, IPlainTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult<Either<Exception, string>>($"context-of-{TrainInput}");
    }
}
