using Trax.Samples.StateMachine.Tests.Fakes;

namespace Trax.Samples.StateMachine.Tests.UnitTests;

/// <summary>
/// The checkout's <c>total</c> is the amount a real <c>ICharge</c> would take. The server owns it: a
/// client cannot save a checkout at all, and a stored draft whose total disagrees with its items (999
/// cents each) cannot be read, the same way a draft with no total cannot.
/// </summary>
public class CheckoutTotalAuthorityTests
{
    private static readonly Guid Id = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    [Test]
    public async Task A_draft_whose_total_disagrees_with_its_items_is_refused()
    {
        var store = new InMemorySnapshotStore();
        var service = new CheckoutMachine().CreateService(store, claims: null);

        // Two items are 1998 cents; the draft says 1.
        store.Seed(
            "alice",
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null,\"total\":1}}"
        );

        (await service.Load("alice", Id)).Should().BeOfType<LoadResult.Invalid>();
    }

    [Test]
    public async Task A_draft_whose_total_matches_its_items_is_read()
    {
        var store = new InMemorySnapshotStore();
        var service = new CheckoutMachine().CreateService(store, claims: null);

        store.Seed(
            "alice",
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null,\"total\":1998}}"
        );

        (await service.Load("alice", Id)).Should().BeOfType<LoadResult.Loaded>();
    }

    [Test]
    public void The_charge_reads_the_amount_from_the_snapshot_total()
    {
        var snapshot = new Snapshot
        {
            Machine = "checkout",
            Version = 2,
            State = "Review",
            Context = new JsonObject
            {
                ["items"] = new JsonArray("book", "pen"),
                ["receipt"] = null,
                ["total"] = 1998,
            },
        };

        CheckoutMachine.AmountCents(snapshot).Should().Be(1998);
    }
}
