using Trax.Samples.StateMachine.E2E.Fixtures;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The client writes the whole checkout snapshot, total included, and the total is what the charge
/// takes. So the server owns it: a draft whose total is not 999 cents per item is refused, and the
/// charge reads the amount from the server's stored, already-checked copy of the draft, never from
/// anything the send request carries.
/// </summary>
[TestFixture]
public class ServerOwnedTotalTests : StateMachineTestFixture
{
    [Test]
    public async Task ADraftWhoseTotalDisagreesWithItsItems_IsInvalidContext_AndNothingIsStored()
    {
        var book = UniqueItem("book");
        var id = Guid.NewGuid();

        // Two items are 1998 cents; the client says 1.
        var saved = await Machines.SaveAsync(
            Alice,
            Checkout,
            id,
            CheckoutWire("Review", [book, "pen"], total: 1)
        );

        saved.ProblemCode.Should().Be("invalid-context");
        (await Machines.LoadAsync(Alice, Checkout, id)).ProblemCode.Should().Be("not-found");
        (await Machines.SendAsync(Alice, Checkout, id)).ProblemCode.Should().Be("not-found");
        Charges.For(book).Should().BeEmpty();
    }

    [Test]
    public async Task ACheapTotalCannotReplaceAValidDraft()
    {
        var book = UniqueItem("book");
        var id = await CheckoutAtReview(book, "pen");

        (
            await Machines.SaveAsync(
                Alice,
                Checkout,
                id,
                CheckoutWire("Review", [book, "pen"], total: 1)
            )
        )
            .ProblemCode.Should()
            .Be("invalid-context");

        (await Machines.LoadAsync(Alice, Checkout, id)).Context["total"]!
            .GetValue<long>()
            .Should()
            .Be(1998);
    }

    [Test]
    public async Task TheCharge_TakesTheTotalOfTheStoredDraft()
    {
        var book = UniqueItem("book");
        var id = await CheckoutAtReview(book, "pen", "lamp");

        var paid = await Machines.SendAsync(Alice, Checkout, id);

        paid.State.Should().Be("Paid");
        var charge = Charges.For(book).Should().ContainSingle().Subject;
        charge.AmountCents.Should().Be(3 * 999);
        charge.Items.Should().Equal(book, "pen", "lamp");
        paid.Context["total"]!.GetValue<long>().Should().Be(3 * 999);
    }
}
