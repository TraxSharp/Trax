using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A parameter the serializer cannot represent is recorded as a placeholder, and the run's outcome
/// is whatever its work produced.
/// </summary>
public class UnserializableParameterRunTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UseInMemory().SaveTrainParameters().AddJunctionProgress()
            )
        );
        services
            .AddScopedTraxRoute<IThrowingGetterOutputTrain, ThrowingGetterOutputTrain>()
            .AddScopedTraxRoute<ICollidingNamesOutputTrain, CollidingNamesOutputTrain>()
            .AddScopedTraxRoute<IThrowingGetterInputTrain, ThrowingGetterInputTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task An_output_with_a_throwing_getter_completes_with_the_placeholder()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IThrowingGetterOutputTrain>();

        await train.Run(Unit.Default);

        var metadata = ((ServiceTrain<Unit, ThrowingGetter>)train).Metadata!;
        metadata.TrainState.Should().Be(TrainState.Completed);
        metadata.Output.Should().Contain("_unserializable");
    }

    [Test]
    public async Task An_output_with_colliding_property_names_completes_with_the_placeholder()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ICollidingNamesOutputTrain>();

        await train.Run(Unit.Default);

        var metadata = ((ServiceTrain<Unit, CollidingNames>)train).Metadata!;
        metadata.TrainState.Should().Be(TrainState.Completed);
        metadata.Output.Should().Contain("_unserializable");
    }

    [Test]
    public async Task An_input_with_a_throwing_getter_completes_with_the_placeholder()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IThrowingGetterInputTrain>();

        await train.Run(new ThrowingGetter());

        var metadata = ((ServiceTrain<ThrowingGetter, Unit>)train).Metadata!;
        metadata.TrainState.Should().Be(TrainState.Completed);
        metadata.Input.Should().Contain("_unserializable");
    }

    public sealed class ThrowingGetter
    {
        public string Broken => throw new InvalidOperationException("getter failed");
    }

    public sealed class CollidingNames
    {
        [JsonPropertyName("value")]
        public int First { get; set; } = 1;

        [JsonPropertyName("value")]
        public int Second { get; set; } = 2;
    }

    public interface IThrowingGetterOutputTrain : IServiceTrain<Unit, ThrowingGetter> { }

    private class ThrowingGetterOutputTrain
        : ServiceTrain<Unit, ThrowingGetter>,
            IThrowingGetterOutputTrain
    {
        protected override Task<Either<Exception, ThrowingGetter>> Junctions() =>
            Task.FromResult<Either<Exception, ThrowingGetter>>(new ThrowingGetter());
    }

    public interface ICollidingNamesOutputTrain : IServiceTrain<Unit, CollidingNames> { }

    private class CollidingNamesOutputTrain
        : ServiceTrain<Unit, CollidingNames>,
            ICollidingNamesOutputTrain
    {
        protected override Task<Either<Exception, CollidingNames>> Junctions() =>
            Task.FromResult<Either<Exception, CollidingNames>>(new CollidingNames());
    }

    public interface IThrowingGetterInputTrain : IServiceTrain<ThrowingGetter, Unit> { }

    private class ThrowingGetterInputTrain
        : ServiceTrain<ThrowingGetter, Unit>,
            IThrowingGetterInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
