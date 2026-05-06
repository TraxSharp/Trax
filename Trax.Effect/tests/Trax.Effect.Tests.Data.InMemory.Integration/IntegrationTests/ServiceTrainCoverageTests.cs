using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Tests targeting branches in ServiceTrain.Run that the existing suite does not exercise:
///   - Junctions() throws OperationCanceledException -> default RunInternal catches and returns Either.Left,
///     which then routes through the "result.IsLeft + cancellation" hook path.
///   - Output cannot be JSON-serialized -> the post-success serialization fallback logs and continues.
/// </summary>
public class ServiceTrainCoverageTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<ICancellingJunctionTrain, CancellingJunctionTrain>()
            .AddScopedTraxRoute<IThrowingHookCancelJunctionTrain, ThrowingHookCancelJunctionTrain>()
            .AddScopedTraxRoute<IUnserializableOutputTrain, UnserializableOutputTrain>()
            .BuildServiceProvider();

    [Test]
    public async Task Run_JunctionThrowsCancellation_RoutesThroughLeftCancellationPath()
    {
        var train = (CancellingJunctionTrain)
            Scope.ServiceProvider.GetRequiredService<ICancellingJunctionTrain>();

        var act = async () => await train.Run("input");
        await act.Should().ThrowAsync<OperationCanceledException>();

        train.OnCancelledCalled.Should().BeTrue();
        ((ServiceTrain<string, int>)train).Metadata!.TrainState.Should().Be(TrainState.Cancelled);
    }

    [Test]
    public async Task Run_JunctionThrowsCancellation_OnCancelledHookThrow_StillPropagates()
    {
        // Covers the inner try/catch around the train-level OnCancelled hook in the
        // Left+cancellation branch.
        var train = Scope.ServiceProvider.GetRequiredService<IThrowingHookCancelJunctionTrain>();

        var act = async () => await train.Run("x");
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task Run_WithMetadataAlreadyInProgress_Throws()
    {
        var train = (CancellingJunctionTrain)
            Scope.ServiceProvider.GetRequiredService<ICancellingJunctionTrain>();
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new Trax.Effect.Models.Metadata.DTOs.CreateMetadata
            {
                Name = typeof(ICancellingJunctionTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = TrainState.InProgress;

        var act = async () => await train.Run("x", metadata);

        await act.Should().ThrowAsync<Trax.Core.Exceptions.TrainException>();
    }

    [Test]
    public async Task Run_OutputNotSerializable_TrainStillCompletes()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IUnserializableOutputTrain>();

        var result = await train.Run(Unit.Default);

        // The train completed successfully even though the post-success
        // output-serialization fallback could not serialize the cyclic graph.
        result.Should().NotBeNull();
        ((ServiceTrain<Unit, Cyclic>)train).Metadata!.TrainState.Should().Be(TrainState.Completed);
    }

    #region Fakes

    private interface ICancellingJunctionTrain : IServiceTrain<string, int> { }

    private class CancellingJunctionTrain : ServiceTrain<string, int>, ICancellingJunctionTrain
    {
        public bool OnCancelledCalled { get; private set; }

        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<CancellingJunction>().Resolve();

        protected override Task OnCancelled(
            Trax.Effect.Models.Metadata.Metadata metadata,
            CancellationToken ct
        )
        {
            OnCancelledCalled = true;
            return Task.CompletedTask;
        }
    }

    private class CancellingJunction : Junction<string, int>
    {
        public override Task<int> Run(string input) =>
            throw new OperationCanceledException("cancel via junction");
    }

    private interface IThrowingHookCancelJunctionTrain : IServiceTrain<string, int> { }

    private class ThrowingHookCancelJunctionTrain
        : ServiceTrain<string, int>,
            IThrowingHookCancelJunctionTrain
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<CancellingJunction>().Resolve();

        protected override Task OnCancelled(
            Trax.Effect.Models.Metadata.Metadata metadata,
            CancellationToken ct
        ) => throw new InvalidOperationException("hook boom");
    }

    public class Cyclic
    {
        public Cyclic? Self { get; set; }
    }

    private interface IUnserializableOutputTrain : IServiceTrain<Unit, Cyclic> { }

    private class UnserializableOutputTrain : ServiceTrain<Unit, Cyclic>, IUnserializableOutputTrain
    {
        protected override async Task<Either<Exception, Cyclic>> RunInternal(Unit input)
        {
            var c = new Cyclic();
            c.Self = c;
            return c;
        }
    }

    #endregion
}
