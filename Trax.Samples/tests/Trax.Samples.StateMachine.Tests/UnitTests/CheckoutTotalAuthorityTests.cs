using Trax.Samples.StateMachine.Tests.Fakes;

namespace Trax.Samples.StateMachine.Tests.UnitTests;

/// <summary>
/// The checkout's <c>total</c> is the amount a real <c>ICharge</c> would take. The server owns it:
/// a client-written draft whose total disagrees with its items (999 cents each) is refused, the
/// same way a draft with no total is.
/// </summary>
public class CheckoutTotalAuthorityTests
{
    private static readonly Guid Id = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    [Test]
    public async Task A_draft_whose_total_disagrees_with_its_items_is_refused()
    {
        var service = new CheckoutMachine().CreateService(
            new InMemorySnapshotStore(),
            claims: null
        );

        // Two items are 1998 cents; the client says 1.
        var result = await service.Autosave(
            "alice",
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null,\"total\":1}}"
        );

        result.Should().BeOfType<AutosaveResult.Rejected>();
    }

    [Test]
    public async Task A_draft_whose_total_matches_its_items_is_saved()
    {
        var service = new CheckoutMachine().CreateService(
            new InMemorySnapshotStore(),
            claims: null
        );

        var result = await service.Autosave(
            "alice",
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null,\"total\":1998}}"
        );

        result.Should().BeOfType<AutosaveResult.Saved>();
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
