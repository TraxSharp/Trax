using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A train resolved once and run twice, which a scoped registration hands back within one scope,
/// records each run. A singleton registration, which would hand one instance to every run in the
/// process, is refused.
///
/// <para>Enforces <c>docs/adr/0011-a-service-train-instance-is-one-run.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0011-a-service-train-instance-is-one-run.md")]
public class TrainInstanceReuseTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IToggleTrain, ToggleTrain>()
            .AddScopedTraxRoute<IExternalIdTrain, ExternalIdTrain>()
            .BuildServiceProvider();

    [Test]
    public async Task A_scoped_train_run_twice_in_one_scope_records_two_runs()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IToggleTrain>();

        var failing = async () => await train.Run(true);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        await train.Run(false);

        var rows = await Rows(typeof(IToggleTrain).FullName!);

        rows.Should()
            .BeEquivalentTo(
                [TrainState.Failed, TrainState.Completed],
                "0011-a-service-train-instance-is-one-run.md: each run is its own execution; the "
                    + "second must not overwrite the first's record"
            );
    }

    [Test]
    public async Task A_scoped_train_run_again_with_its_own_external_id_keeps_it()
    {
        var train = (ExternalIdTrain)Scope.ServiceProvider.GetRequiredService<IExternalIdTrain>();

        await train.Run(false);
        var first = train.Metadata!.ExternalId;
        train.ExternalId = "chosen-for-the-second-run".PadRight(32, '0');
        await train.Run(false);

        train
            .Metadata!.ExternalId.Should()
            .Be(
                train.ExternalId,
                "0011-a-service-train-instance-is-one-run.md: an ExternalId set for a run is that run's"
            );
        first.Should().NotBe(train.ExternalId);
        (await Rows(typeof(IExternalIdTrain).FullName!)).Should().HaveCount(2);
    }

    [Test]
    public void A_service_train_cannot_be_registered_as_a_singleton()
    {
        // A singleton train would be one instance, one metadata row and one data context shared by
        // every run in the process, concurrent ones included.
        var generic = () =>
            new ServiceCollection().AddSingletonTraxRoute<
                ISingletonToggleTrain,
                SingletonToggleTrain
            >();
        var runtime = () =>
            new ServiceCollection().AddSingletonTraxRoute(
                typeof(ISingletonToggleTrain),
                typeof(SingletonToggleTrain)
            );

        const string because =
            "0011-a-service-train-instance-is-one-run.md refuses a singleton service train";
        generic.Should().Throw<InvalidOperationException>(because).WithMessage("*singleton*");
        runtime.Should().Throw<InvalidOperationException>(because).WithMessage("*singleton*");
    }

    private async Task<List<TrainState>> Rows(string name)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == name)
            .OrderBy(m => m.Id)
            .Select(m => m.TrainState)
            .ToListAsync();
    }

    private class ToggleTrain : ServiceTrain<bool, Unit>, IToggleTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(
                TrainInput ? new InvalidOperationException("asked to fail") : Unit.Default
            );
    }

    private class SingletonToggleTrain : ToggleTrain, ISingletonToggleTrain { }

    private class ExternalIdTrain : ToggleTrain, IExternalIdTrain { }

    public interface IToggleTrain : IServiceTrain<bool, Unit> { }

    public interface ISingletonToggleTrain : IServiceTrain<bool, Unit> { }

    public interface IExternalIdTrain : IServiceTrain<bool, Unit> { }
}
