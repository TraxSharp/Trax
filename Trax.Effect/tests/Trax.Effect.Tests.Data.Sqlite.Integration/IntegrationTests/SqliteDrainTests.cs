using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.Drain;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The drain (Trax.Docs/adr/0065) on Sqlite: the row is set, read, lapses and is deleted, and the busy list's query runs
/// on this provider and answers as it does on Postgres.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SqliteDrainTests : TestSetup
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public async Task A_drain_is_set_held_until_its_time_and_cleared()
    {
        var context = await Context();
        await using var disposal = context.ConfigureAwait(false);
        await DrainStore.ClearAsync(context, Now, CancellationToken.None);
        context.Reset();

        await DrainStore.SetAsync(
            context,
            Now.AddMinutes(20),
            Now,
            "deploy",
            CancellationToken.None
        );
        context.Reset();
        await DrainStore.SetAsync(
            context,
            Now.AddMinutes(30),
            Now,
            "operator",
            CancellationToken.None
        );
        context.Reset();

        (await DrainStore.IsDrainedAsync(context, Now.AddMinutes(29), CancellationToken.None))
            .Should()
            .BeTrue();
        (await DrainStore.IsDrainedAsync(context, Now.AddMinutes(30), CancellationToken.None))
            .Should()
            .BeFalse("the drain lapses at its time");
        (await DrainStore.ReadAsync(context, Now, CancellationToken.None))
            ?.StartedBy.Should()
            .Be("operator");

        (await DrainStore.ClearAsync(context, Now, CancellationToken.None)).Should().BeTrue();
        context.Reset();
        (await context.Drains.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task The_busy_list_counts_live_runs_and_instances_not_at_rest()
    {
        var context = await Context();
        await using var disposal = context.ConfigureAwait(false);
        var tag = Guid.NewGuid().ToString("N");
        var live = await Run(context, TrainState.InProgress, tag);
        var queued = await Run(context, TrainState.Pending, tag);
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = queued.Id },
            TimeProvider.System
        );
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();
        context.SnapshotDrafts.Add(Instance(tag, live.ExternalId));
        context.SnapshotDrafts.Add(Instance(tag, queued.ExternalId));
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var (items, _) = await DrainStore.BusyAsync(context, [], CancellationToken.None);

        items
            .Where(i => i.Name.EndsWith(tag, StringComparison.Ordinal))
            .Select(i => i.Kind)
            .Should()
            .BeEquivalentTo([DrainBusyItem.Run, DrainBusyItem.Instance]);
    }

    private async Task<IDataContext> Context() =>
        await Scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);

    private static async Task<Metadata> Run(IDataContext context, TrainState state, string tag)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = $"Drain.{tag}",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            },
            TimeProvider.System
        );
        metadata.TrainState = state;
        await context.Track(metadata).ConfigureAwait(false);
        await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        context.Reset();
        return metadata;
    }

    private static SnapshotDraft Instance(string tag, string invokeToken) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerKind = SnapshotOwnerKind.System,
            Machine = tag,
            Version = 1,
            State = "Invoking",
            ConcurrencyToken = Guid.NewGuid(),
            UpdatedAt = TimeProvider.System.GetUtcNow(),
            InvokeToken = invokeToken,
        };
}
