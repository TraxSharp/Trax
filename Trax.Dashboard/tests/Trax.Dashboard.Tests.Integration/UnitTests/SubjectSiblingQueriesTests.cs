using FluentAssertions;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// What the work queue detail page means by "Waiting On" has to be what dispatch would actually do.
/// </summary>
/// <remarks>
/// The page told an operator which sibling was ahead of their entry by comparing priority and age.
/// Dispatch drops candidates first, so the page named entries dispatch would never have offered and
/// an operator was told to wait for something that was not coming.
/// </remarks>
[TestFixture]
public class SubjectSiblingQueriesTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A queued entry. <c>Id</c> is database-generated and private-set by design, and the query
    /// excludes the entry itself by id, so the test assigns one through the backing property rather
    /// than widening the model for a test's convenience.
    /// </summary>
    private static WorkQueue Entry(
        long id,
        int priority = 0,
        int ageMinutes = 0,
        DateTime? scheduledAt = null,
        string? subject = "customer-1",
        bool confirmed = true
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "MyApp.IOrderTrain",
                InputTypeName = "MyApp.OrderInput",
                SubjectKey = subject,
                Priority = priority,
                ScheduledAt = scheduledAt,
            }
        );

        entry.Status = WorkQueueStatus.Queued;
        entry.CreatedAt = Now.AddMinutes(-ageMinutes);
        entry.ConfirmedAt = confirmed ? Now.AddMinutes(-ageMinutes) : null;

        typeof(WorkQueue).GetProperty(nameof(WorkQueue.Id))!.SetValue(entry, id);

        return entry;
    }

    private static List<long> AheadOf(WorkQueue entry, params WorkQueue[] all) =>
        all.AsQueryable().DispatchedAheadOf(entry, Now).Select(b => b.Id).ToList();

    /// <summary>The case the old comparison got wrong.</summary>
    [Test]
    public void ASiblingScheduledForLater_IsNotAheadOfAnEntryDueNow()
    {
        var dueNow = Entry(id: 2, priority: 0, ageMinutes: 1);
        var scheduledTomorrow = Entry(
            id: 1,
            priority: 5,
            ageMinutes: 10,
            scheduledAt: Now.AddDays(1)
        );

        AheadOf(dueNow, dueNow, scheduledTomorrow)
            .Should()
            .BeEmpty(
                "dispatch drops an entry whose scheduled time has not arrived, so it cannot be "
                    + "what this entry is waiting for however high its priority"
            );
    }

    [Test]
    public void ASiblingScheduledInThePast_IsAheadOfIt()
    {
        var entry = Entry(id: 2, priority: 0, ageMinutes: 1);
        var overdue = Entry(id: 1, priority: 5, ageMinutes: 10, scheduledAt: Now.AddMinutes(-5));

        AheadOf(entry, entry, overdue).Should().Equal([1]);
    }

    [Test]
    public void AHigherPrioritySibling_IsAheadOfIt()
    {
        var entry = Entry(id: 2, priority: 0, ageMinutes: 10);
        var higher = Entry(id: 1, priority: 5, ageMinutes: 1);

        AheadOf(entry, entry, higher)
            .Should()
            .Equal(
                [1],
                "priority leads age, so a younger higher-priority sibling still goes first"
            );
    }

    [Test]
    public void AnOlderSiblingAtTheSamePriority_IsAheadOfIt()
    {
        var entry = Entry(id: 2, ageMinutes: 1);
        var older = Entry(id: 1, ageMinutes: 10);

        AheadOf(entry, entry, older).Should().Equal([1]);
    }

    [Test]
    public void AYoungerSiblingAtTheSamePriority_IsNotAheadOfIt()
    {
        var entry = Entry(id: 1, ageMinutes: 10);
        var younger = Entry(id: 2, ageMinutes: 1);

        AheadOf(entry, entry, younger).Should().BeEmpty();
    }

    [Test]
    public void AnUnconfirmedSibling_IsNotAheadOfIt()
    {
        var entry = Entry(id: 2, ageMinutes: 1);
        var staged = Entry(id: 1, priority: 5, ageMinutes: 10, confirmed: false);

        AheadOf(entry, entry, staged)
            .Should()
            .BeEmpty("a staged entry is not a dispatch candidate until its hook confirms it");
    }

    [Test]
    public void ASiblingForAnotherSubject_IsNotAheadOfIt()
    {
        var entry = Entry(id: 2, ageMinutes: 1);
        var other = Entry(id: 1, priority: 5, ageMinutes: 10, subject: "customer-2");

        AheadOf(entry, entry, other).Should().BeEmpty();
    }

    [Test]
    public void SiblingsAreOrderedTheWayDispatchWouldTakeThem()
    {
        var entry = Entry(id: 9, ageMinutes: 0);
        var oldLow = Entry(id: 1, priority: 0, ageMinutes: 30);
        var newHigh = Entry(id: 2, priority: 9, ageMinutes: 1);
        var oldHigh = Entry(id: 3, priority: 9, ageMinutes: 20);

        AheadOf(entry, entry, oldLow, newHigh, oldHigh)
            .Should()
            .Equal([3, 2, 1], "priority descending, then oldest first");
    }
}
