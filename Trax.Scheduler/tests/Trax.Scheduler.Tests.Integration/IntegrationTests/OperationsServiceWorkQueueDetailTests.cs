using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="IOperationsService.GetWorkQueueEntryDetailAsync"/>, the one read behind the
/// dashboard's work queue entry page and the GraphQL API's <c>workQueue.detail</c>: the entry's
/// fields, its input masked, and what a queued entry with a subject is waiting on.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceWorkQueueDetailTests : TestSetup
{
    private IOperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    private Task<WorkQueueEntryDetail?> Detail(long id) =>
        _operations.GetWorkQueueEntryDetailAsync(id, CancellationToken.None);

    private async Task<long> AddEntry(
        string? subject,
        WorkQueueStatus status = WorkQueueStatus.Queued,
        int priority = 0,
        long? metadataId = null,
        DateTime? createdAt = null,
        DateTime? scheduledAt = null,
        string? input = null,
        string? inputTypeName = null
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ISchedulerTestTrain).FullName!,
                InputTypeName = inputTypeName ?? "Other.App.SubjectInput",
                Input = input,
                SubjectKey = subject,
                Priority = priority,
                ScheduledAt = scheduledAt,
            }
        );
        entry.Status = status;
        entry.MetadataId = metadataId;
        if (createdAt is { } at)
            entry.CreatedAt = at;
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry.Id;
    }

    private async Task<long> AddRun(TrainState state)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = state;
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return run.Id;
    }

    [Test]
    public async Task The_detail_carries_every_field_and_the_input_read_as_its_type()
    {
        var id = await AddEntry(
            "customer-1",
            priority: 4,
            input: """{"value":"visible"}""",
            inputTypeName: typeof(SchedulerTestInput).FullName
        );

        var detail = await Detail(id);

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(id);
        detail.TrainName.Should().Be(typeof(ISchedulerTestTrain).FullName);
        detail.Status.Should().Be(WorkQueueStatus.Queued);
        detail.Priority.Should().Be(4);
        detail.SubjectKey.Should().Be("customer-1");
        detail.InputTypeName.Should().Be(typeof(SchedulerTestInput).FullName);
        detail.ConfirmedAt.Should().NotBeNull();
        detail.Input.Should().Contain("visible").And.NotContain("_redacted");
        detail.SubjectHeldBy.Should().BeNull();
        detail.SubjectQueuedBehind.Should().BeNull();
    }

    [Test]
    public async Task An_input_this_host_cannot_read_as_its_type_is_masked_whole()
    {
        var id = await AddEntry("customer-1", input: """{"secret":"s3cr3t"}""");

        var detail = await Detail(id);

        detail!.Input.Should().Be("""{"_redacted":true}""");
    }

    [Test]
    public async Task A_subject_with_a_run_in_flight_names_the_entry_holding_it()
    {
        var running = await AddRun(TrainState.InProgress);
        var holder = await AddEntry("customer-2", WorkQueueStatus.Dispatched, metadataId: running);
        var older = await AddEntry("customer-2", createdAt: DateTime.UtcNow.AddMinutes(-5));
        var waiting = await AddEntry("customer-2");

        var detail = await Detail(waiting);

        detail!.SubjectHeldBy.Should().Be(holder);
        older.Should().BePositive();
        detail
            .SubjectQueuedBehind.Should()
            .BeNull("an entry held by a running sibling says so, not what is queued ahead");
    }

    [Test]
    public async Task A_subject_whose_run_finished_is_not_held()
    {
        var done = await AddRun(TrainState.Completed);
        await AddEntry("customer-3", WorkQueueStatus.Dispatched, metadataId: done);
        var waiting = await AddEntry("customer-3");

        var detail = await Detail(waiting);

        detail!.SubjectHeldBy.Should().BeNull();
        detail.SubjectQueuedBehind.Should().BeNull();
    }

    [Test]
    public async Task An_older_or_higher_priority_sibling_is_what_it_queues_behind()
    {
        var older = await AddEntry("customer-4", createdAt: DateTime.UtcNow.AddMinutes(-5));
        var younger = await AddEntry("customer-4");
        var urgent = await AddEntry(
            "customer-5",
            createdAt: DateTime.UtcNow.AddMinutes(1),
            priority: 9
        );
        var ordinary = await AddEntry("customer-5");

        (await Detail(younger))!.SubjectQueuedBehind.Should().Be(older);
        (await Detail(older))!.SubjectQueuedBehind.Should().BeNull();
        (await Detail(ordinary))!.SubjectQueuedBehind.Should().Be(urgent);
    }

    [Test]
    public async Task A_sibling_not_yet_due_is_not_ahead_of_it()
    {
        await AddEntry("customer-6", priority: 9, scheduledAt: DateTime.UtcNow.AddDays(1));
        var dueNow = await AddEntry("customer-6");

        (await Detail(dueNow))!
            .SubjectQueuedBehind.Should()
            .BeNull("dispatch does not offer an entry before it is due");
    }

    [Test]
    public async Task An_entry_with_no_subject_or_not_queued_names_nothing()
    {
        var running = await AddRun(TrainState.InProgress);
        await AddEntry("customer-7", WorkQueueStatus.Dispatched, metadataId: running);
        var dispatched = await AddEntry("customer-7", WorkQueueStatus.Dispatched);
        var noSubject = await AddEntry(subject: null);

        var d1 = await Detail(dispatched);
        var d2 = await Detail(noSubject);

        d1!.SubjectHeldBy.Should().BeNull();
        d1.SubjectQueuedBehind.Should().BeNull();
        d2!.SubjectHeldBy.Should().BeNull();
        d2.SubjectQueuedBehind.Should().BeNull();
    }

    [Test]
    public async Task A_missing_id_is_null() => (await Detail(99_999_999)).Should().BeNull();
}
