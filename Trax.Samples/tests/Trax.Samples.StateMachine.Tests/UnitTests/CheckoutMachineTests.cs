using Trax.Samples.StateMachine.Tests.Fakes;

namespace Trax.Samples.StateMachine.Tests.UnitTests;

/// <summary>
/// Drives the sample <see cref="CheckoutMachine"/> through the real persistence service over an in-memory
/// store. Covers the v1 -> v2 forward migration (the headline of the versioned sample), the guard that makes
/// that migration load-bearing, the FE-drives/BE-validates flow, and the canonical wire.
/// </summary>
public class CheckoutMachineTests
{
    private static readonly Guid Id = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private const string User = "alice";

    private static ISnapshotDraftService Service(InMemorySnapshotStore store) =>
        new CheckoutMachine().CreateService(store, claims: null);

    [Test]
    public async Task Load_migrates_a_stored_v1_draft_to_v2_and_backfills_total()
    {
        var store = new InMemorySnapshotStore();
        // A draft an older host wrote at v1: items + receipt, and NO total.
        store.Seed(
            User,
            Id,
            "{\"machine\":\"checkout\",\"version\":1,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null}}"
        );

        var loaded = (await Service(store).Load(User, Id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot;

        loaded.Version.Should().Be(2);
        loaded.State.Should().Be("Review");
        loaded.Context["items"]!.AsArray().Should().HaveCount(2);
        // Backfilled from the item count (2 items x 999 cents), so the v2 total guard passes.
        loaded.Context["total"]!
            .GetValue<int>()
            .Should()
            .Be(1998);
    }

    [Test]
    public async Task A_v2_draft_without_a_numeric_total_is_rejected()
    {
        // This is what makes the migration load-bearing: under v2 a pre-total context is invalid, so a v1
        // draft is only usable because Load backfills total.
        var store = new InMemorySnapshotStore();
        store.Seed(
            User,
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[],\"receipt\":null}}"
        );

        (await Service(store).Load(User, Id)).Should().BeOfType<LoadResult.Invalid>();
    }

    [Test]
    public async Task A_client_cannot_save_a_checkout()
    {
        // The generic save serves only machines with no server-side behaviour, and the checkout has an effect.
        (
            await Service(new InMemorySnapshotStore())
                .Autosave(
                    User,
                    Id,
                    "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[\"book\"],\"receipt\":null,\"total\":999}}"
                )
        )
            .Should()
            .BeOfType<AutosaveResult.Rejected>()
            .Which.Code.Should()
            .Be(RefusalCode.SaveNotAllowed);
    }

    [Test]
    public async Task A_stored_cart_advances_to_review()
    {
        var store = new InMemorySnapshotStore();
        var service = Service(store);
        store.Seed(
            User,
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[\"book\"],\"receipt\":null,\"total\":999}}"
        );

        var advanced = (await service.Advance(User, Id, "Next"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot;

        advanced.State.Should().Be("Review");
        advanced.Context["total"]!.GetValue<int>().Should().Be(999);
    }

    [Test]
    public void The_checkout_is_declarative_so_it_exports_an_IR()
    {
        var machine = new CheckoutMachine();

        var ir = machine.ExportIr();

        machine
            .SchemaHash.Should()
            .NotBeNull("an exportable IR is what its version is hashed from");
        ir.Should()
            .Contain(CheckoutMachine.AddItemToCart)
            .And.Contain(
                CheckoutMachine.TotalMatchesItems,
                "the custom reduction and rule are carried by name"
            );
    }

    [Test]
    public async Task AddItem_appends_the_item_and_the_server_prices_the_cart()
    {
        var store = new InMemorySnapshotStore();
        var service = Service(store);
        store.Seed(
            User,
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[],\"receipt\":null,\"total\":0}}"
        );

        await service.Advance(User, Id, "AddItem", JsonNode.Parse("{\"item\":\"book\"}"));
        var cart = (
            await service.Advance(User, Id, "AddItem", JsonNode.Parse("{\"item\":\"pen\"}"))
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot;

        cart.State.Should().Be("Cart");
        cart.Context["items"]!
            .AsArray()
            .Select(i => i!.GetValue<string>())
            .Should()
            .Equal("book", "pen");
        cart.Context["total"]!.GetValue<long>().Should().Be(2 * CheckoutMachine.UnitPriceCents);
    }

    [Test]
    public async Task An_item_with_no_name_and_a_review_of_an_empty_cart_are_refused()
    {
        var store = new InMemorySnapshotStore();
        var service = Service(store);
        store.Seed(
            User,
            Id,
            "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[],\"receipt\":null,\"total\":0}}"
        );

        (await service.Advance(User, Id, "AddItem", JsonNode.Parse("{\"item\":\"\"}")))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.GuardFailed);
        (await service.Advance(User, Id, "Next"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.GuardFailed);
    }

    [Test]
    public void Serialize_emits_canonical_wire_with_ordinally_sorted_context_keys()
    {
        var snapshot = new Snapshot
        {
            Machine = "checkout",
            Version = 2,
            State = "Cart",
            // Deliberately out of order; the canonical wire must sort them (items < receipt < total).
            Context = new JsonObject
            {
                ["total"] = 0,
                ["receipt"] = null,
                ["items"] = new JsonArray(),
            },
        };

        Service(new InMemorySnapshotStore())
            .Serialize(snapshot)
            .Should()
            .Be(
                "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Cart\",\"context\":{\"items\":[],\"receipt\":null,\"total\":0}}"
            );
    }
}
