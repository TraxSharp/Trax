using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The failure data a run's exception carries names the train by its canonical name, the one its
/// metadata row, the dashboard and the registry use (the interface's full name), wherever in the
/// run the failure happened.
/// </summary>
public class FailureTrainNameTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        services
            .AddSingleton<IFailureClassifier>(new AlwaysPermanent())
            .AddScopedTraxRoute<IFailsInJunctionTrain, FailsInJunctionTrain>()
            .AddScopedTraxRoute<IFailsOutsideJunctionTrain, FailsOutsideJunctionTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_failure_inside_a_junction_carries_the_canonical_name()
    {
        var data = await FailureData<IFailsInJunctionTrain>();

        data.TrainName.Should().Be(typeof(IFailsInJunctionTrain).FullName);
    }

    [Test]
    public async Task A_failure_outside_any_junction_carries_the_canonical_name()
    {
        var data = await FailureData<IFailsOutsideJunctionTrain>();

        data.TrainName.Should().Be(typeof(IFailsOutsideJunctionTrain).FullName);
    }

    private async Task<TrainExceptionData> FailureData<TTrain>()
        where TTrain : IServiceTrain<Unit, Unit>
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<TTrain>();

        var act = async () => await train.Run(Unit.Default);
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();

        return thrown
            .Which.Data["TrainExceptionData"]
            .Should()
            .BeOfType<TrainExceptionData>()
            .Subject;
    }

    private sealed class AlwaysPermanent : IFailureClassifier
    {
        public FailureClass? Classify(Exception exception) => FailureClass.Permanent;
    }

    public interface IFailsInJunctionTrain : IServiceTrain<Unit, Unit>;

    public class FailsInJunctionTrain : ServiceTrain<Unit, Unit>, IFailsInJunctionTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ThrowsJunction>().Resolve();

        public class ThrowsJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input) =>
                throw new InvalidOperationException("inside a junction");
        }
    }

    public interface IFailsOutsideJunctionTrain : IServiceTrain<Unit, Unit>;

    public class FailsOutsideJunctionTrain : ServiceTrain<Unit, Unit>, IFailsOutsideJunctionTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            throw new InvalidOperationException("outside any junction");
    }
}
