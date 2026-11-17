using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Monad;
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

    private ServiceProvider? _provider;

    private string? _dbPath;

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
                effects.UseSqlite($"Data Source={_dbPath.NotNull()}").AddJunctionEvents()
            )
        );
        services.AddScopedTraxRoute<ILiteStepsTrain, LiteStepsTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider()
    {
        await _provider.NotNull().DisposeAsync();

        foreach (
            var path in new[]
            {
                _dbPath.NotNull(),
                _dbPath.NotNull() + "-wal",
                _dbPath.NotNull() + "-shm",
            }
        )
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

    [Test]
    public async Task Each_row_stores_the_id_the_trains_graph_draws_for_its_step()
    {
        ChainGraph graph;
        var scope = _provider.NotNull().CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var fresh = (LiteStepsTrain)scope.ServiceProvider.GetRequiredService<ILiteStepsTrain>();
            graph = ChainGraph.From(
                fresh.DeclaredChain(),
                fresh.GetType(),
                typeof(LiteItem),
                typeof(string)
            );
        }

        var metadataId = await Run();

        var rows = await Rows(metadataId);
        rows.Select(r => r.NodeId)
            .Should()
            .Equal(
                graph.Nodes[0].Id,
                graph.Nodes.Single(n => n.Kind == ChainStepKind.Decide).Id,
                graph.Nodes.Single(n => n.Kind == ChainStepKind.Switch).Id,
                graph
                    .Nodes.Single(n => n.Kind == ChainStepKind.Switch)
                    .Tracks.Single(t =>
                        string.Equals(t.Name, nameof(LiteBin.Small), StringComparison.Ordinal)
                    )
                    .Nodes[0]
                    .Id
            );
        rows[3].NodeId.Should().Be("Switch<LiteBin>#0/Small/LiteBreak#0");

        await Delete(metadataId);
    }

    private async Task<long> Run()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (LiteStepsTrain)scope.ServiceProvider.GetRequiredService<ILiteStepsTrain>();
        var run = async () =>
            await train.Run(new LiteItem(3), CancellationToken.None).ConfigureAwait(false);
        await run.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        await _provider
            .NotNull()
            .GetRequiredService<JunctionRunWriter>()
            .FlushAsync(CancellationToken.None)
            .ConfigureAwait(false);
        return train.Metadata.NotNull().Id;
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var factory = _provider.NotNull().GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .JunctionRuns.AsNoTracking()
            .ForRun(metadataId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task Delete(long metadataId)
    {
        var factory = _provider.NotNull().GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        await context
            .Metadatas.Where(m => m.Id == metadataId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
