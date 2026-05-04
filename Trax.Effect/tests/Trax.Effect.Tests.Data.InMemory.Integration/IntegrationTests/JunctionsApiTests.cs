using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

public class JunctionsApiTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IJunctionsTrain, JunctionsTrain>()
            .AddScopedTraxRoute<IJunctionsMultiTrain, JunctionsMultiTrain>()
            .AddScopedTraxRoute<IJunctionsFailingTrain, JunctionsFailingTrain>()
            .AddScopedTraxRoute<IRunInternalTrain, RunInternalTrain>()
            .BuildServiceProvider();

    #region Junctions API — happy path

    [Test]
    public async Task Junctions_SingleChain_ReturnsOutput()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionsTrain>();

        var result = await train.Run("hello");

        result.Should().Be(5);
    }

    [Test]
    public async Task Junctions_MultipleChains_ReturnsOutput()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionsMultiTrain>();

        var result = await train.Run("hello");

        result.Should().Be("5");
    }

    [Test]
    public async Task Junctions_MetadataTracked()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionsTrain>();

        await train.Run("hello");

        var serviceTrain = (ServiceTrain<string, int>)train;
        serviceTrain.Metadata.Should().NotBeNull();
        serviceTrain.Metadata!.TrainState.Should().Be(TrainState.Completed);
    }

    #endregion

    #region Junctions API — failure path

    [Test]
    public async Task Junctions_JunctionThrows_SetsFailedState()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IJunctionsFailingTrain>();

        var act = async () => await train.Run("hello");

        await act.Should().ThrowAsync<Exception>();

        var serviceTrain = (ServiceTrain<string, int>)train;
        serviceTrain.Metadata.Should().NotBeNull();
        serviceTrain.Metadata!.TrainState.Should().Be(TrainState.Failed);
    }

    #endregion

    #region Backwards compatibility

    [Test]
    public async Task RunInternal_Override_StillWorks()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IRunInternalTrain>();

        var result = await train.Run("hello");

        result.Should().Be(5);
    }

    [Test]
    public async Task RunInternal_MetadataTracked()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IRunInternalTrain>();

        await train.Run("hello");

        var serviceTrain = (ServiceTrain<string, int>)train;
        serviceTrain.Metadata.Should().NotBeNull();
        serviceTrain.Metadata!.TrainState.Should().Be(TrainState.Completed);
    }

    #endregion

    #region Fakes

    private class StringLengthJunction : Junction<string, int>
    {
        public override async Task<int> Run(string input) => input.Length;
    }

    private class IntToStringJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }

    private class ThrowingJunction : Junction<string, int>
    {
        public override Task<int> Run(string input) =>
            throw new InvalidOperationException("junction failed");
    }

    private class JunctionsTrain : ServiceTrain<string, int>, IJunctionsTrain
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<StringLengthJunction>().Resolve();
    }

    private interface IJunctionsTrain : IServiceTrain<string, int> { }

    private class JunctionsMultiTrain : ServiceTrain<string, string>, IJunctionsMultiTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<StringLengthJunction>().Chain<IntToStringJunction>().Resolve();
    }

    private interface IJunctionsMultiTrain : IServiceTrain<string, string> { }

    private class JunctionsFailingTrain : ServiceTrain<string, int>, IJunctionsFailingTrain
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<ThrowingJunction>().Resolve();
    }

    private interface IJunctionsFailingTrain : IServiceTrain<string, int> { }

    private class RunInternalTrain : ServiceTrain<string, int>, IRunInternalTrain
    {
        protected override Task<Either<Exception, int>> RunInternal(string input) =>
            Activate(input).Chain<StringLengthJunction>().Resolve();
    }

    private interface IRunInternalTrain : IServiceTrain<string, int> { }

    #endregion
}
