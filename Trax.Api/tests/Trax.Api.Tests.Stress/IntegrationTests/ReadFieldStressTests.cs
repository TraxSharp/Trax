using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// The reads added so a frontend can be built on the API alone: the manifest list's group name
/// (a join onto <c>manifest_group</c>), <c>manifestDetail</c>, <c>workQueue.detail</c> with its
/// two subject lookups, the effects list with settings, and the host config reads. Each is
/// measured against the same millions-of-rows seed as <see cref="AdminEndpointStressTests"/>.
/// </summary>
/// <remarks>
/// The bulk seed carries no subject keys, so the fixture adds a slice of entries that do: one in
/// ten of them dispatched against a seeded execution, the rest queued, spread over a thousand
/// subjects. That is the shape the subject lookups have to stay fast on.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class ReadFieldStressTests : StressTestSetup
{
    private const int Subjects = 1_000;

    private static long _subjectRows;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    private static ITrainDiscoveryService Discovery(IServiceProvider sp) =>
        sp.GetRequiredService<ITrainDiscoveryService>();

    // What HotChocolate injects into the list resolvers: the provider's dialect, whose row
    // estimate stands in for an exact count of an unfiltered table.
    private static ISqlDialect Dialect(IServiceProvider sp) => sp.GetRequiredService<ISqlDialect>();

    private static async Task EnsureSubjectRowsAsync(IServiceProvider sp, CancellationToken ct)
    {
        await SeedLock.WaitAsync(ct);
        try
        {
            using var db = await Factory(sp).CreateDbContextAsync(ct);
            var ctx = (DbContext)db;
            var existing = await db.WorkQueues.CountAsync(q => q.SubjectKey != null, ct);
            var target = Math.Max(10_000, Profile.WorkQueue / 10);
            if (existing < target)
            {
                ctx.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                var missing = target - existing;
                var metadataRows = Profile.Metadata;
                await ctx.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO trax.work_queue (external_id, train_name, status, created_at,
                        priority, dispatch_attempts, subject_key, confirmed_at, metadata_id)
                    SELECT 'wqs-' || g, 'Trax.Stress.Trains.IStressTrain1',
                           CASE WHEN g % 10 = 0 THEN 'dispatched'::trax.work_queue_status
                                ELSE 'queued'::trax.work_queue_status END,
                           now() - ((g % 20160) * interval '1 minute'),
                           (g % 32), 0, 'subject-' || (g % {Subjects}), now(),
                           CASE WHEN g % 10 = 0 THEN 1 + (g % {metadataRows}) ELSE NULL END
                    FROM generate_series(1, {missing}) g
                    """,
                    ct
                );
                await ctx.Database.ExecuteSqlRawAsync("ANALYZE trax.work_queue", ct);
            }
            _subjectRows = await db.WorkQueues.CountAsync(q => q.SubjectKey != null, ct);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task<long> QueuedEntryFor(
        IServiceProvider sp,
        string subject,
        CancellationToken ct
    )
    {
        await EnsureSubjectRowsAsync(sp, ct);
        using var db = await Factory(sp).CreateDbContextAsync(ct);
        return await db
            .WorkQueues.Where(q => q.SubjectKey == subject && q.Status == WorkQueueStatus.Queued)
            .OrderByDescending(q => q.Id)
            .Select(q => q.Id)
            .FirstAsync(ct);
    }

    [Test]
    public async Task Manifests_WithGroupName_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifests (first page, +group name)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetManifests(
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.Items.Should().OnlyContain(m => m.ManifestGroupName != null);
            }
        );
    }

    [Test]
    public async Task Manifests_WithGroupName_KeysetDeep_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifests (keyset, far end, +group name)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetManifests(
                    Factory(sp),
                    ct,
                    take: 25,
                    afterId: 30,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(m => m.ManifestGroupName != null);
            }
        );
    }

    [Test]
    public async Task ManifestDetail_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestDetail",
            ListBudget,
            async (sp, ct) =>
            {
                var detail = await new OperationsQueries().GetManifestDetail(
                    Profile.Manifests / 2,
                    Factory(sp),
                    Discovery(sp),
                    ct
                );
                detail.Should().NotBeNull();
                detail!.ManifestGroupName.Should().StartWith("stress-group-");
            }
        );
    }

    [Test]
    public async Task WorkQueueDetail_SubjectHeld_WithinBudget()
    {
        long id = 0;
        await MeasureAsync(
            "operations.workQueue.detail (subject lookups)",
            ListBudget,
            async (sp, ct) =>
            {
                if (id == 0)
                    id = await QueuedEntryFor(sp, "subject-7", ct);
                var detail = await new WorkQueueQueries().GetDetail(
                    id,
                    Factory(sp),
                    Discovery(sp),
                    ct
                );
                detail.Should().NotBeNull();
                detail!.SubjectKey.Should().Be("subject-7");
            }
        );
        TestContext.Out.WriteLine($"subject-keyed work_queue rows: {_subjectRows:N0}");
    }

    [Test]
    public async Task WorkQueueDetail_SubjectWithNothingAhead_WithinBudget()
    {
        // The worst case for the queued-behind lookup: a lowest-priority, newest entry whose
        // subject has no other queued entry. No index leads with subject_key for queued rows,
        // so the lookup walks the whole queued, confirmed index (ix_work_queue_status_priority)
        // before it can answer "nothing". Its cost grows with the queue depth.
        long id = 0;
        await MeasureAsync(
            "operations.workQueue.detail (subject, nothing ahead)",
            ListBudget,
            async (sp, ct) =>
            {
                if (id == 0)
                {
                    await EnsureSubjectRowsAsync(sp, ct);
                    using var db = await Factory(sp).CreateDbContextAsync(ct);
                    var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
                        new Trax.Effect.Models.WorkQueue.DTOs.CreateWorkQueue
                        {
                            TrainName = "Trax.Stress.Trains.IStressTrain1",
                            SubjectKey = "subject-alone-" + Guid.NewGuid().ToString("N"),
                            Priority = 0,
                        }
                    );
                    await db.Track(entry);
                    await db.SaveChanges(ct);
                    id = entry.Id;
                }
                var detail = await new WorkQueueQueries().GetDetail(
                    id,
                    Factory(sp),
                    Discovery(sp),
                    ct
                );
                detail!.SubjectQueuedBehind.Should().BeNull();
                detail.SubjectHeldBy.Should().BeNull();
            }
        );
    }

    [Test]
    public async Task WorkQueueDetail_NoSubject_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.detail (no subject)",
            ListBudget,
            async (sp, ct) =>
            {
                var detail = await new WorkQueueQueries().GetDetail(
                    Profile.WorkQueue / 2,
                    Factory(sp),
                    Discovery(sp),
                    ct
                );
                detail.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task Effects_WithSettings_WithinBudget()
    {
        await MeasureAsync(
            "operations.effects (+settings)",
            TrivialBudget,
            (sp, _) =>
            {
                var effects = new OperationsQueries().GetEffects(
                    sp.GetRequiredService<IEffectRegistry>(),
                    sp
                );
                effects.Should().NotBeEmpty();
                return Task.CompletedTask;
            }
        );
    }

    [Test]
    public async Task ConfigEnvironmentAndLogLevels_WithinBudget()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" }
            )
            .Build();
        var environment = new StressEnvironment();

        await MeasureAsync(
            "operations.config.environmentName + logLevels",
            TrivialBudget,
            (_, _) =>
            {
                new ConfigQueries().GetEnvironmentName(environment).Should().Be("Stress");
                new ConfigQueries().GetLogLevels(configuration).Should().ContainSingle();
                return Task.CompletedTask;
            }
        );
    }

    private sealed class StressEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Stress";
        public string ApplicationName { get; set; } = "Trax.Api.Tests.Stress";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
