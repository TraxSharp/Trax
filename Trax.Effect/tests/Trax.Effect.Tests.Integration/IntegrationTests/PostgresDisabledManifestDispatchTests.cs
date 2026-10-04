using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A disabled manifest pauses the work it already queued: the candidate load and the claim both
/// pass over its scheduled entries, and dispatch an explicit trigger for it. The claim tests the
/// manifest without joining it, so <c>FOR UPDATE</c> locks only the entry.
///
/// <para>Enforces <c>docs/adr/0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md"
)]
public class PostgresDisabledManifestDispatchTests : TestSetup
{
    [Test]
    public async Task A_scheduled_entry_of_a_disabled_manifest_is_neither_loaded_nor_claimed()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await NewContext();

        var entry = await QueueFor(context, manifestEnabled: false, explicitTrigger: false);

        (await Candidates(context, dialect)).Should().NotContain(entry.Id);
        (await Claim(context, dialect, entry.Id))
            .Should()
            .BeNull(
                "the manifest is disabled, and nobody asked for this run by name ("
                    + "0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md)"
            );
    }

    [Test]
    public async Task A_scheduled_entry_of_an_enabled_manifest_is_loaded_and_claimed()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await NewContext();

        var entry = await QueueFor(context, manifestEnabled: true, explicitTrigger: false);

        (await Candidates(context, dialect)).Should().Contain(entry.Id);
        (await Claim(context, dialect, entry.Id)).Should().NotBeNull();
    }

    [Test]
    public async Task An_explicit_trigger_of_a_disabled_manifest_is_loaded_and_claimed()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await NewContext();

        var entry = await QueueFor(context, manifestEnabled: false, explicitTrigger: true);

        (await Candidates(context, dialect)).Should().Contain(entry.Id);
        (await Claim(context, dialect, entry.Id))
            .Should()
            .NotBeNull(
                "an operator asked for this run by name ("
                    + "0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md)"
            );
    }

    [Test]
    public async Task A_scheduled_entry_marked_explicit_after_it_was_queued_is_released()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await NewContext();

        var entry = await QueueFor(context, manifestEnabled: false, explicitTrigger: false);
        entry.IsExplicitTrigger = true;
        await context.SaveChanges(CancellationToken.None);

        (await Claim(context, dialect, entry.Id)).Should().NotBeNull();
    }

    [Test]
    public async Task An_explicit_trigger_in_a_disabled_group_is_still_held()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await NewContext();

        var entry = await QueueFor(
            context,
            manifestEnabled: false,
            explicitTrigger: true,
            groupEnabled: false
        );

        (await Candidates(context, dialect)).Should().NotContain(entry.Id);
    }

    /// <summary>
    /// <c>FOR UPDATE</c> locks a row of every table in the claim's <c>FROM</c> list, so a claim that
    /// joined the manifest would lock it too, and with <c>SKIP LOCKED</c> would pass over every
    /// entry of a manifest someone else holds, such as a scheduler updating it.
    /// </summary>
    [Test]
    public async Task The_claim_does_not_lock_or_wait_for_the_manifest_row()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var holder = await NewContext();
        var entry = await QueueFor(holder, manifestEnabled: true, explicitTrigger: false);

        var holderDb = ((DbContext)holder).Database;
        await using var held = await holderDb.BeginTransactionAsync();
        await holderDb.ExecuteSqlRawAsync(
            "SELECT id FROM trax.manifest WHERE id = {0} FOR UPDATE",
            entry.ManifestId!.Value
        );

        using var claimer = await NewContext();
        var claimerDb = ((DbContext)claimer).Database;
        await using var claim = await claimerDb.BeginTransactionAsync();
        await claimerDb.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'");

        (await Claim(claimer, dialect, entry.Id))
            .Should()
            .NotBeNull("the manifest is only read by the claim, never locked by it");
    }

    private async Task<IDataContext> NewContext() =>
        await DataContextFactory.CreateDbContextAsync(CancellationToken.None);

    private static async Task<WorkQueue> QueueFor(
        IDataContext context,
        bool manifestEnabled,
        bool explicitTrigger,
        bool groupEnabled = true
    )
    {
        var group = new ManifestGroup
        {
            Name = $"disabled-dispatch-{Guid.NewGuid():N}",
            IsEnabled = groupEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(PostgresDisabledManifestDispatchTests) }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.IsEnabled = manifestEnabled;
        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                ManifestId = manifest.Id,
                ExplicitTrigger = explicitTrigger,
            }
        );
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);

        return entry;
    }

    private static async Task<List<long>> Candidates(IDataContext context, ISqlDialect dialect) =>
        await context
            .WorkQueues.FromSqlRaw(dialect.LoadGroupFairQueuedJobs(), 1000)
            .AsNoTracking()
            .Select(w => w.Id)
            .ToListAsync();

    private static async Task<WorkQueue?> Claim(
        IDataContext context,
        ISqlDialect dialect,
        long id
    ) =>
        await context
            .WorkQueues.FromSqlRaw(dialect.ClaimWorkQueueEntry(), id)
            .AsNoTracking()
            .FirstOrDefaultAsync();
}
