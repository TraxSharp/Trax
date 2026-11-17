using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// A service train whose questions are asked in <c>Parallel</c> branches, against Sqlite: the
/// rebuilt <c>decision</c> table keys an asking by its branch, so two branches asking one question
/// each get a row and a requeue replays each, and each step's row carries its branch.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SqliteParallelRunTests
{
    private ServiceProvider? _provider;

    private static readonly LiteParDecider Decider = new();

    private string? _dbPath;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_parallel_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(Decider);
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UseSqlite($"Data Source={_dbPath.NotNull()}")
                    .AddJunctionEvents()
                    .AddDecisionRecording()
            )
        );
        services.AddScopedTraxRoute<ILiteParAskEverywhereTrain, LiteParAskEverywhereTrain>();
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
    public async Task Branches_asking_one_question_each_get_a_row_and_a_requeue_replays_each()
    {
        Decider.Script(
            new(StringComparer.Ordinal)
            {
                [""] = [LiteParLane.Express, LiteParLane.Ground],
                ["Parallel#0/a"] = [LiteParLane.Ground],
                ["Parallel#0/b"] = [LiteParLane.Express],
            }
        );

        var original = await Run();

        (await Recorded(original))
            .Select(r => (r.BranchPath, r.Occurrence, r.Tracks().Single()))
            .Should()
            .BeEquivalentTo(
                [
                    ("", 0, "Express"),
                    ("Parallel#0/a", 1, "Ground"),
                    ("Parallel#0/b", 1, "Express"),
                    ("", 1, "Ground"),
                ],
                "the branches count on from the fork, and the unique key tells them apart by branch"
            );

        Decider.Script([]);
        var requeued = await Run(replayDecisionsOf: original);

        Decider.Asked.Should().Be(0);
        LiteParTook.Of(requeued).Should().BeEquivalentTo(LiteParTook.Of(original));
        (await Recorded(requeued)).Should().HaveCount(4).And.OnlyContain(r => r.Replayed);
    }

    [Test]
    public async Task Each_step_row_carries_the_branch_it_ran_in()
    {
        Decider.Script([]);

        var run = await Run();

        var rows = await Rows(run);
        rows.Where(r => string.Equals(r.Name, nameof(LiteParTookGround), StringComparison.Ordinal))
            .Select(r => r.BranchPath)
            .Should()
            .BeEquivalentTo([null, "Parallel#0/a", "Parallel#0/b", null]);
        rows.Single(r => string.Equals(r.Name, nameof(LiteParFinish), StringComparison.Ordinal))
            .BranchPath.Should()
            .BeNull();
    }

    private async Task<long> Run(long? replayDecisionsOf = null)
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<LiteParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<ILiteParAskEverywhereTrain>();
        var input = new LiteParOrder("o");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ILiteParAskEverywhereTrain).FullName.NotNull(),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        await train.Run(input, metadata, CancellationToken.None).ConfigureAwait(false);
        await _provider
            .NotNull()
            .GetRequiredService<JunctionRunWriter>()
            .FlushAsync(CancellationToken.None)
            .ConfigureAwait(false);
        return train.Metadata.NotNull().Id;
    }

    private async Task<List<Models.RecordedDecision.RecordedDecision>> Recorded(long metadataId)
    {
        var factory = _provider.NotNull().GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .ToListAsync()
            .ConfigureAwait(false);
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
}
