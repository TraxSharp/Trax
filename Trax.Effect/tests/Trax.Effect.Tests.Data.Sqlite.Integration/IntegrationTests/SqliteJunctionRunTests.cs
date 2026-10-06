using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// <c>AddJunctionEvents</c> against Sqlite: a run's steps are <c>junction_run</c> rows, with their
/// kind, state and failure class stored as the enums' integers, read back in order, and deleted with
/// their run.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
public class SqliteJunctionRunTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private ServiceProvider _provider = null!;

    private string _dbPath = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_junction_runs_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(new ScriptedDecider().Choose(LiteBin.Small, 0.75));
        services.AddSingleton<IFailureClassifier, LiteStepClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.UseSqlite($"Data Source={_dbPath}").AddJunctionEvents()
            )
        );
        services.AddScopedTraxRoute<ILiteStepsTrain, LiteStepsTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider()
    {
        await _provider.DisposeAsync();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(path))
                File.Delete(path);
    }

    [Test]
    public async Task A_runs_steps_are_rows_read_back_in_order()
    {
        var metadataId = await Run();

        var rows = await Rows(metadataId);
        rows.Select(r => (r.Position, r.Kind, r.Name, r.State))
            .Should()
            .Equal(
                (0, JunctionRunKind.Junction, nameof(LiteMeasure), JunctionRunState.Completed),
                (1, JunctionRunKind.Choice, "LiteBin", JunctionRunState.Completed),
                (2, JunctionRunKind.Route, "LiteBin", JunctionRunState.Completed),
                (3, JunctionRunKind.Junction, nameof(LiteBreak), JunctionRunState.Failed)
            );
        rows[1].Answer.Should().Be("Small");
        rows[1].Confidence.Should().Be(0.75);
        rows[3].FailureClass.Should().Be(FailureClass.Conflict);
        rows[3].FailureException.Should().Be(nameof(InvalidOperationException));
        rows[3].EndedAt.Should().BeOnOrAfter(rows[3].StartedAt);

        await Delete(metadataId);
    }

    [Test]
    public async Task A_runs_steps_are_deleted_with_it()
    {
        var metadataId = await Run();
        (await Rows(metadataId)).Should().NotBeEmpty();

        await Delete(metadataId);

        (await Rows(metadataId)).Should().BeEmpty($"the foreign key cascades. See {Adr}.");
    }

    private async Task<long> Run()
    {
        using var scope = _provider.CreateScope();
        var train = (LiteStepsTrain)scope.ServiceProvider.GetRequiredService<ILiteStepsTrain>();
        var run = async () => await train.Run(new LiteItem(3));
        await run.Should().ThrowAsync<InvalidOperationException>();

        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return train.Metadata!.Id;
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync();
    }

    private async Task Delete(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await context.Metadatas.Where(m => m.Id == metadataId).ExecuteDeleteAsync();
    }
}

public sealed record LiteItem(int Size);

[Asks("Which bin does this item go in?")]
public enum LiteBin
{
    Small,
    Large,
}

public class LiteMeasure : EffectJunction<LiteItem, LiteItem>
{
    public override Task<LiteItem> Run(LiteItem input) => Task.FromResult(input);
}

public class LiteBreak : EffectJunction<LiteItem, string>
{
    public override Task<string> Run(LiteItem input) =>
        throw new InvalidOperationException("the bin is jammed");
}

public sealed class LiteStepClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is InvalidOperationException ? FailureClass.Conflict : null;
}

public interface ILiteStepsTrain : IServiceTrain<LiteItem, string>;

public class LiteStepsTrain : ServiceTrain<LiteItem, string>, ILiteStepsTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<LiteMeasure>()
            .Switch<LiteItem, LiteBin>(tracks =>
                tracks
                    .When(LiteBin.Small, t => t.Chain<LiteBreak>())
                    .When(LiteBin.Large, t => t.Chain<LiteBreak>())
            )
            .Resolve();
}
