using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// Tests for the GraphQL <c>operations.workQueue</c> namespace.
/// <para>
/// Queries (<see cref="WorkQueueQueries"/>) are exercised against a real Postgres
/// instance because they're SQL-bound. Mutations (<see cref="WorkQueueMutations"/>)
/// are thin wrappers around <see cref="IOperationsService"/>; their behaviour is
/// tested deeply in <c>Trax.Scheduler.Tests.Integration.OperationsServiceTests</c>,
/// so here we only verify the GraphQL layer correctly forwards calls and translates
/// <see cref="OperationResult"/> into <see cref="Trax.Api.DTOs.OperationResponse"/>.
/// </para>
/// </summary>
[TestFixture]
public class WorkQueueOperationsTests
{
    // Pool tuning copied from the AuthE2E hardening (PR #41) after the same
    // class of CI flake. Dropping Connection Pruning Interval=1 + Idle Lifetime=1
    // lets the pool reuse connections across tests instead of paying TCP+auth
    // every SetUp, which is what was timing out under CI Postgres contention.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_api_workqueue;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (Microsoft.EntityFrameworkCore.DbContext)db;
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.work_queue, trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    private async Task SeedWorkQueues(
        int count,
        WorkQueueStatus status = WorkQueueStatus.Queued,
        string? trainName = null
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = trainName ?? $"Trax.Tests.IFakeTrain{i}",
                    Priority = i % 4,
                }
            );
            entry.Status = status;
            await db.Track(entry);
        }
        await db.SaveChanges(default);
    }

    #region CancelWorkQueueEntries (batch)

    [Test]
    public async Task CancelWorkQueueEntries_CancelsOnlyQueuedInSet()
    {
        await SeedWorkQueues(3, WorkQueueStatus.Queued);
        await SeedWorkQueues(1, WorkQueueStatus.Dispatched);

        long[] ids;
        await using (var db = await _factory.CreateDbContextAsync(default))
            ids = await db.WorkQueues.Select(q => q.Id).ToArrayAsync();

        var resp = await new WorkQueueMutations().CancelWorkQueueEntries(ids, _factory, default);

        resp.Success.Should().BeTrue();
        resp.Count.Should().Be(3); // only the 3 queued; the dispatched one is skipped
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var cancelled = await db.WorkQueues.CountAsync(q =>
                q.Status == WorkQueueStatus.Cancelled
            );
            cancelled.Should().Be(3);
        }
    }

    [Test]
    public async Task CancelWorkQueueEntries_EmptyIds_ReturnsZero()
    {
        var resp = await new WorkQueueMutations().CancelWorkQueueEntries([], _factory, default);

        resp.Success.Should().BeTrue();
        resp.Count.Should().Be(0);
    }

    #endregion

    #region GetWorkQueues

    [Test]
    public async Task GetWorkQueues_NoData_ReturnsEmpty()
    {
        var queries = new WorkQueueQueries();

        var result = await queries.GetWorkQueues(_factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task GetWorkQueues_PaginatesAndExposesCursor()
    {
        await SeedWorkQueues(5);
        var queries = new WorkQueueQueries();

        var result = await queries.GetWorkQueues(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetWorkQueues_AfterIdCursor_FiltersByCursorAndUsesExactCount()
    {
        await SeedWorkQueues(5);
        var queries = new WorkQueueQueries();
        var first = await queries.GetWorkQueues(_factory, default, take: 2);

        var page2 = await queries.GetWorkQueues(
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(q => q.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
        page2.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetWorkQueues_SkipPagination_HonorsSkip()
    {
        await SeedWorkQueues(5);
        var queries = new WorkQueueQueries();

        var result = await queries.GetWorkQueues(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
        result.Skip.Should().Be(2);
    }

    [Test]
    public async Task GetWorkQueues_StatusFilter_OnlyMatchingEntries()
    {
        await SeedWorkQueues(2, WorkQueueStatus.Queued);
        await SeedWorkQueues(3, WorkQueueStatus.Cancelled);
        var queries = new WorkQueueQueries();

        var queued = await queries.GetWorkQueues(_factory, default, status: WorkQueueStatus.Queued);
        var cancelled = await queries.GetWorkQueues(
            _factory,
            default,
            status: WorkQueueStatus.Cancelled
        );

        queued.Items.Should().HaveCount(2);
        queued.Items.Should().OnlyContain(q => q.Status == WorkQueueStatus.Queued);
        cancelled.Items.Should().HaveCount(3);
        cancelled.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetWorkQueues_TrainNameFilter_OnlyMatchingEntries()
    {
        await SeedWorkQueues(2, trainName: "Trax.Tests.IAlpha");
        await SeedWorkQueues(3, trainName: "Trax.Tests.IBeta");
        var queries = new WorkQueueQueries();

        var alpha = await queries.GetWorkQueues(_factory, default, trainName: "Trax.Tests.IAlpha");

        alpha.Items.Should().HaveCount(2);
        alpha.Items.Should().OnlyContain(q => q.TrainName == "Trax.Tests.IAlpha");
    }

    [Test]
    public async Task GetWorkQueues_WhitespaceTrainName_TreatedAsNoFilter()
    {
        await SeedWorkQueues(3);
        var queries = new WorkQueueQueries();

        var result = await queries.GetWorkQueues(_factory, default, trainName: "   ");

        result.Items.Should().HaveCount(3);
    }

    [Test]
    public async Task GetWorkQueues_StatusAndAfterId_BothApplied()
    {
        await SeedWorkQueues(2, WorkQueueStatus.Queued);
        await SeedWorkQueues(3, WorkQueueStatus.Cancelled);
        var queries = new WorkQueueQueries();
        var firstCancelled = await queries.GetWorkQueues(
            _factory,
            default,
            status: WorkQueueStatus.Cancelled,
            take: 1
        );

        var page2 = await queries.GetWorkQueues(
            _factory,
            default,
            status: WorkQueueStatus.Cancelled,
            take: 5,
            afterId: firstCancelled.NextCursor
        );

        page2.Items.Should().OnlyContain(q => q.Status == WorkQueueStatus.Cancelled);
        page2
            .Items.Select(q => q.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(firstCancelled.NextCursor!.Value));
    }

    [Test]
    public async Task GetWorkQueue_ById_ReturnsRow()
    {
        await SeedWorkQueues(2);
        var queries = new WorkQueueQueries();
        var first = (await queries.GetWorkQueues(_factory, default)).Items.First();

        var fetched = await queries.GetWorkQueue(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
        fetched.ExternalId.Should().Be(first.ExternalId);
    }

    [Test]
    public async Task GetWorkQueue_AllSummaryFields_PopulatedFromRow()
    {
        var when = DateTime.UtcNow.AddMinutes(15);
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = "Trax.Tests.IRichTrain",
                    InputTypeName = "Trax.Tests.RichInput",
                    Priority = 7,
                    ScheduledAt = when,
                }
            );
            entry.Status = WorkQueueStatus.Dispatched;
            entry.DispatchedAt = DateTime.UtcNow.AddMinutes(-1);
            entry.DispatchAttempts = 2;
            await db.Track(entry);
            await db.SaveChanges(default);
        }

        var queries = new WorkQueueQueries();
        var first = (await queries.GetWorkQueues(_factory, default)).Items.First();
        var fetched = await queries.GetWorkQueue(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
        fetched.ExternalId.Should().NotBeNullOrEmpty();
        fetched.TrainName.Should().Be("Trax.Tests.IRichTrain");
        fetched.Status.Should().Be(WorkQueueStatus.Dispatched);
        fetched.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        fetched.DispatchedAt.Should().NotBeNull();
        fetched.ScheduledAt.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
        fetched.Priority.Should().Be(7);
        fetched.DispatchAttempts.Should().Be(2);
        fetched.ManifestId.Should().BeNull();
        fetched.MetadataId.Should().BeNull();
        fetched.DeadLetterId.Should().BeNull();
        fetched.InputTypeName.Should().Be("Trax.Tests.RichInput");
    }

    [Test]
    public async Task GetWorkQueue_MissingId_ReturnsNull()
    {
        var queries = new WorkQueueQueries();

        (await queries.GetWorkQueue(99999, _factory, default)).Should().BeNull();
    }

    #endregion

    #region Mutation pass-through

    // The mutations are thin wrappers around IOperationsService. We only need to verify
    // the wrapper forwards arguments correctly and maps OperationResult -> OperationResponse.
    // Behavioural tests live in OperationsServiceTests in Trax.Scheduler.Tests.Integration.

    [Test]
    public async Task QueueTrain_ForwardsToOperationsService_AndMapsSuccess()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Id: 42, Count: 1, Message: "ok"));
        var mutations = new WorkQueueMutations();
        var input = new QueueTrainInput("Trax.Tests.IFake", "{}", Priority: 3);

        var response = await mutations.QueueTrain(input, ops, default);

        response.Success.Should().BeTrue();
        response.Count.Should().Be(1);
        response.Message.Should().Be("ok");
        await ops.Received(1).QueueTrainAsync(input, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QueueTrain_ForwardsToOperationsService_AndMapsFailure()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(false, Message: "Unknown train: nope"));
        var mutations = new WorkQueueMutations();

        var response = await mutations.QueueTrain(new QueueTrainInput("nope"), ops, default);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Unknown train");
    }

    [Test]
    public async Task CancelWorkQueueEntry_ForwardsToOperationsService_AndMapsSuccess()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.CancelWorkQueueEntryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Id: 7, Count: 1, Message: "cancelled"));
        var mutations = new WorkQueueMutations();

        var response = await mutations.CancelWorkQueueEntry(7, ops, default);

        response.Success.Should().BeTrue();
        response.Count.Should().Be(1);
        await ops.Received(1).CancelWorkQueueEntryAsync(7, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelWorkQueueEntry_ForwardsToOperationsService_AndMapsFailure()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.CancelWorkQueueEntryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(false, Message: "Work queue entry 999 not found."));
        var mutations = new WorkQueueMutations();

        var response = await mutations.CancelWorkQueueEntry(999, ops, default);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("not found");
    }

    #endregion

    #region Namespace wiring

    [Test]
    public void OperationsQueries_WorkQueueNamespace_ReturnsNewInstance()
    {
        var queries = new OperationsQueries();
        queries.WorkQueue().Should().NotBeNull();
    }

    [Test]
    public void OperationsMutations_WorkQueueNamespace_ReturnsNewInstance()
    {
        var mutations = new OperationsMutations();
        mutations.WorkQueue().Should().NotBeNull();
    }

    #endregion
}
