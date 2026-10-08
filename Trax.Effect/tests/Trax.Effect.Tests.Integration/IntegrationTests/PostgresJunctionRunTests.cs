using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>AddJunctionEvents</c> against Postgres: a run's steps are <c>trax.junction_run</c> rows,
/// with their kind, state and failure class in the Trax enum types, read back in order, and
/// deleted with their run.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
public class PostgresJunctionRunTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(new ScriptedDecider().Choose(PgBin.Small, 0.75));
        services.AddSingleton<IFailureClassifier, PgStepClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJunctionEvents())
        );
        services.AddScopedTraxRoute<IPgStepsTrain, PgStepsTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_runs_steps_are_rows_read_back_in_order()
    {
        var metadataId = await Run();

        var rows = await Rows(metadataId);
        rows.Select(r => (r.Position, r.Kind, r.Name, r.State))
            .Should()
            .Equal(
                (0, JunctionRunKind.Junction, nameof(PgMeasure), JunctionRunState.Completed),
                (1, JunctionRunKind.Choice, "PgBin", JunctionRunState.Completed),
                (2, JunctionRunKind.Route, "PgBin", JunctionRunState.Completed),
                (3, JunctionRunKind.Junction, nameof(PgBreak), JunctionRunState.Failed)
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
    public async Task A_manifests_run_stores_its_attempt()
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        long manifestId;
        using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            var group = new ManifestGroup
            {
                Name = $"junction-runs-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await context.Track(group);
            await context.SaveChanges(CancellationToken.None);

            var manifest = Manifest.Create(new CreateManifest { Name = typeof(IPgStepsTrain) });
            manifest.ManifestGroupId = group.Id;
            await context.Track(manifest);
            await context.SaveChanges(CancellationToken.None);
            manifestId = manifest.Id;
        }

        var first = await Run(manifestId);
        var second = await Run(manifestId);
        var third = await Run(manifestId);

        (await Rows(first)).Should().NotBeEmpty().And.OnlyContain(r => r.Attempt == 1);
        (await Rows(second)).Should().NotBeEmpty().And.OnlyContain(r => r.Attempt == 2);
        (await Rows(third))
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(r => r.Attempt == 3, $"two failed runs came before it. See {Adr}.");

        foreach (var id in new[] { first, second, third })
            await Delete(id);
    }

    [Test]
    public async Task A_burst_of_steps_is_written_in_batches_and_every_end_lands()
    {
        var metadataId = await Run();
        var writer = _provider.GetRequiredService<JunctionRunWriter>();
        var dropped = writer.DroppedSteps;
        var start = DateTime.UtcNow;
        const int Junctions = 1500;

        // Several batches' worth of starts, then their ends, which update rows written by an
        // earlier batch as well as by their own.
        for (var position = 100; position < 100 + Junctions; position++)
            writer.Write(
                metadataId,
                new JunctionEventPayload(
                    position,
                    JunctionRunKind.Junction,
                    "Burst",
                    JunctionRunState.InProgress,
                    start
                )
            );
        for (var position = 100; position < 100 + Junctions; position++)
            writer.Write(
                metadataId,
                new JunctionEventPayload(
                    position,
                    JunctionRunKind.Junction,
                    "Burst",
                    JunctionRunState.Completed,
                    start,
                    start.AddMilliseconds(1),
                    1
                )
            );
        await writer.FlushAsync();

        writer.DroppedSteps.Should().Be(dropped);
        var burst = (await Rows(metadataId)).Where(r => r.Name == "Burst").ToList();
        burst
            .Should()
            .HaveCount(Junctions)
            .And.OnlyContain(
                r => r.State == JunctionRunState.Completed && r.EndedAt != null,
                $"an end updates the row its start wrote. See {Adr}."
            );

        await Delete(metadataId);
    }

    [Test]
    public async Task Each_row_stores_the_id_the_trains_graph_draws_for_its_step()
    {
        ChainGraph graph;
        using (var scope = _provider.CreateScope())
        {
            var fresh = (PgStepsTrain)scope.ServiceProvider.GetRequiredService<IPgStepsTrain>();
            graph = ChainGraph.From(
                fresh.DeclaredChain(),
                fresh.GetType(),
                typeof(PgItem),
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
                    .Tracks.Single(t => t.Name == nameof(PgBin.Small))
                    .Nodes[0]
                    .Id
            );
        rows[3].NodeId.Should().Be("Switch<PgBin>#0/Small/PgBreak#0");

        await Delete(metadataId);
    }

    private async Task<long> Run(long? manifestId = null)
    {
        using var scope = _provider.CreateScope();
        var train = (PgStepsTrain)scope.ServiceProvider.GetRequiredService<IPgStepsTrain>();
        var input = new PgItem(3);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IPgStepsTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ManifestId = manifestId,
            }
        );
        var run = async () => await train.Run(input, metadata);
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

public sealed record PgItem(int Size);

[Asks("Which bin does this item go in?")]
public enum PgBin
{
    Small,
    Large,
}

public class PgMeasure : EffectJunction<PgItem, PgItem>
{
    public override Task<PgItem> Run(PgItem input) => Task.FromResult(input);
}

public class PgBreak : EffectJunction<PgItem, string>
{
    public override Task<string> Run(PgItem input) =>
        throw new InvalidOperationException("the bin is jammed");
}

public sealed class PgStepClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is InvalidOperationException ? FailureClass.Conflict : null;
}

public interface IPgStepsTrain : IServiceTrain<PgItem, string>;

public class PgStepsTrain : ServiceTrain<PgItem, string>, IPgStepsTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgMeasure>()
            .Switch<PgItem, PgBin>(tracks =>
                tracks
                    .When(PgBin.Small, t => t.Chain<PgBreak>())
                    .When(PgBin.Large, t => t.Chain<PgBreak>())
            )
            .Resolve();
}
