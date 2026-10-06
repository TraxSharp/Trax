using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A run whose last junction finishes its work after the caller cancelled is recorded
/// <c>Completed</c> (docs/adr/0005), with junction progress on as well as off.
///
/// <para>Enforces <c>docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md")]
public class JunctionProgressCancellationTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddJunctionProgress())
        );
        services.AddScopedTraxRoute<IFinishesAnywayTrain, FinishesAnywayTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task Work_that_finished_after_the_caller_cancelled_is_recorded_completed_with_junction_progress()
    {
        using var scope = _provider.CreateScope();
        var train = (FinishesAnywayTrain)
            scope.ServiceProvider.GetRequiredService<IFinishesAnywayTrain>();
        using var cts = new CancellationTokenSource();
        Downstream.Reset();

        var run = train.Run(Unit.Default, cts.Token);
        await Downstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await cts.CancelAsync();
        Downstream.Answer.SetResult();

        try
        {
            await run;
        }
        catch (OperationCanceledException) { }

        Downstream.WorkDone.Should().BeTrue("the junction's downstream call completed");

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();
        var row = await context
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == train.Metadata!.Id);

        row.TrainState.Should()
            .Be(
                TrainState.Completed,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md records work that finished despite the caller's cancellation as "
                    + "Completed; the progress write after the junction must not turn it into Cancelled"
            );
    }

    private static class Downstream
    {
        public static TaskCompletionSource Started { get; private set; } = New();
        public static TaskCompletionSource Answer { get; private set; } = New();
        public static bool WorkDone { get; set; }

        public static void Reset()
        {
            Started = New();
            Answer = New();
            WorkDone = false;
        }

        private static TaskCompletionSource New() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class UninterruptibleJunction : EffectJunction<Unit, Unit>
    {
        public override async Task<Unit> Run(Unit input)
        {
            Downstream.Started.TrySetResult();
            await Downstream.Answer.Task;
            Downstream.WorkDone = true;
            return Unit.Default;
        }
    }

    private class FinishesAnywayTrain : ServiceTrain<Unit, Unit>, IFinishesAnywayTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<UninterruptibleJunction>().Resolve();
    }

    public interface IFinishesAnywayTrain : IServiceTrain<Unit, Unit> { }
}
