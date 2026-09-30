using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task TrainBus_InputWithNoTrain_TellsTheHostWhereItLookedAndHowToFixIt()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = async () => await bus.RunAsync<Unit>(new InputWithNoTrain());

        var thrown = (await act.Should().ThrowAsync<TrainException>()).Which;
        thrown.Message.Should().Contain(typeof(InputWithNoTrain).FullName!);
        thrown.Message.Should().Contain(typeof(RunAndQueueExecutionTests).Assembly.GetName().Name);
        thrown.Message.Should().Contain("ScanAssemblies(");
    }

    [Test]
    public async Task TrainBus_InputWithNoTrain_UnderAHostRegistry_DoesNotClaimAScan()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(RunAndQueueExecutionTests).Assembly])
        );
        services.AddSingleton<ITrainRegistry>(new EmptyRegistry());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = async () => await bus.RunAsync<Unit>(new InputWithNoTrain());

        var thrown = (await act.Should().ThrowAsync<TrainException>()).Which;
        thrown.Message.Should().Contain(typeof(InputWithNoTrain).FullName!);
        thrown
            .Message.Should()
            .NotContain(
                "Scanned assemblies",
                "a registry the host supplied scanned nothing, so there is no list to report"
            );
    }

    private sealed class EmptyRegistry : ITrainRegistry
    {
        public Dictionary<Type, Type> InputTypeToTrain { get; set; } = [];
    }

    /// <summary>No train in any scanned assembly takes this.</summary>
    public record InputWithNoTrain;
}
