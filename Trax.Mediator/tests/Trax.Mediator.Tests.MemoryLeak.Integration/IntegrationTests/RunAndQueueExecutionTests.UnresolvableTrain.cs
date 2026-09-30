using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task RunAsync_TrainThatCannotBeResolved_LeavesNoPendingMetadata()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () =>
            execution.RunAsync(typeof(IUnresolvableTrain).FullName!, """{"label":"x"}""");
        await act.Should().ThrowAsync<InvalidOperationException>();

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var rows = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == typeof(IUnresolvableTrain).FullName)
            .ToListAsync();

        rows.Should()
            .NotContain(
                m => m.TrainState == TrainState.Pending,
                "the run failed before the train existed, so nothing will ever move the row on"
            );
    }

    public record UnresolvableInput
    {
        public string Label { get; init; } = "";
    }

    /// <summary>Never registered, the way a dependency a host forgot to add is not.</summary>
    public interface INeverRegistered;

    public interface IUnresolvableTrain : IServiceTrain<UnresolvableInput, Unit>;

    public class UnresolvableTrain(INeverRegistered dependency)
        : ServiceTrain<UnresolvableInput, Unit>,
            IUnresolvableTrain
    {
        public INeverRegistered Dependency { get; } = dependency;

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
