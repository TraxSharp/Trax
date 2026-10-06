using Trax.Samples.StateMachine.E2E.Fixtures;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The checkout, the machine with an effect, driven over GraphQL: <c>Cart → Review</c> by advance,
/// <c>Review → Paid</c> only by <c>sendSnapshot</c>, which runs the charge, and a committed
/// <c>Paid</c> that a client can neither reach nor rewrite by saving.
/// </summary>
[TestFixture]
public class CheckoutTests : StateMachineTestFixture
{
    [Test]
    public async Task Cart_Next_Review_Back_Cart()
    {
        var item = UniqueItem("book");
        var id = Guid.NewGuid();
        (await Machines.SaveAsync(Alice, Checkout, id, CheckoutWire("Cart", [item])))
            .State.Should()
            .Be("Cart");

        var review = await Machines.AdvanceAsync(Alice, Checkout, id, "Next");
        review.State.Should().Be("Review");
        review.Context["total"]!.GetValue<long>().Should().Be(999);

        (await Machines.AdvanceAsync(Alice, Checkout, id, "Back")).State.Should().Be("Cart");
    }

    [Test]
    public async Task Send_ChargesOnce_AndMovesToPaidWithTheReceipt()
    {
        var item = UniqueItem("book");
        var id = await CheckoutAtReview(item);

        var paid = await Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1");

        paid.State.Should().Be("Paid");
        var charge = Charges.For(item).Should().ContainSingle().Subject;
        charge.State.Should().Be("Review");
        charge.AmountCents.Should().Be(999);
        paid.Context["receipt"]!.GetValue<string>().Should().Be(charge.Receipt);
        (await Machines.LoadAsync(Alice, Checkout, id)).Wire.Should().Be(paid.Wire);
    }

    [Test]
    public async Task ASendRepeated_ReturnsTheSamePaidSnapshot_AndDoesNotChargeAgain()
    {
        var item = UniqueItem("book");
        var id = await CheckoutAtReview(item);

        var first = await Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1");
        var second = await Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1");

        second.Wire.Should().Be(first.Wire);
        Charges.For(item).Should().ContainSingle("the second send is a retry, not a second charge");
    }

    [Test]
    public async Task TwoSendsAtOnce_ChargeOnce()
    {
        // Two tabs press Pay at the same moment. At most one send runs the charge; the other replays
        // it or is told the effect is in progress, and a later send returns the one Paid snapshot.
        var item = UniqueItem("book");
        var id = await CheckoutAtReview(item);

        var results = await Task.WhenAll(
            Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1"),
            Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1")
        );

        foreach (var result in results)
            if (result.ProblemCode is not null)
                result.ProblemCode.Should().BeOneOf("effect-in-progress", "conflict");
        var settled = await Machines.SendAsync(Alice, Checkout, id, requestId: "pay-1");
        settled.State.Should().Be("Paid");
        Charges.For(item).Should().ContainSingle();
        settled.Context["receipt"]!.GetValue<string>().Should().Be(Charges.For(item)[0].Receipt);
    }

    [Test]
    public async Task AdvancingPay_IsEffectBound_SoOnlyASendCanCharge()
    {
        var item = UniqueItem("book");
        var id = await CheckoutAtReview(item);

        var advanced = await Machines.AdvanceAsync(
            Alice,
            Checkout,
            id,
            "Pay",
            input: """{"receipt":"rcpt_forged"}"""
        );

        advanced.ProblemCode.Should().Be("effect-bound");
        Charges.For(item).Should().BeEmpty();
        (await Machines.LoadAsync(Alice, Checkout, id)).State.Should().Be("Review");
    }

    [Test]
    public async Task SavingADraftStraightIntoPaid_IsStateReserved()
    {
        var item = UniqueItem("book");
        var id = Guid.NewGuid();

        var saved = await Machines.SaveAsync(
            Alice,
            Checkout,
            id,
            CheckoutWire("Paid", [item], receipt: "rcpt_forged")
        );

        saved.ProblemCode.Should().Be("state-reserved");
        (await Machines.LoadAsync(Alice, Checkout, id)).ProblemCode.Should().Be("not-found");
        Charges.For(item).Should().BeEmpty();
    }

    [Test]
    public async Task APaidDraft_CannotBeRewrittenBySaving_ButCanBeReset()
    {
        var item = UniqueItem("book");
        var id = await CheckoutAtReview(item);
        var paid = await Machines.SendAsync(Alice, Checkout, id);

        // Back to Review, as a client trying to pay twice would write it.
        (await Machines.SaveAsync(Alice, Checkout, id, CheckoutWire("Review", [item])))
            .ProblemCode.Should()
            .Be("draft-committed");
        (await Machines.LoadAsync(Alice, Checkout, id)).Wire.Should().Be(paid.Wire);

        // The machine declares Paid --Reset--> Cart, so a reset is the one write it allows.
        var reset = await Machines.AdvanceAsync(Alice, Checkout, id, "Reset");
        reset.State.Should().Be("Cart");
        reset.Context["items"]!.AsArray().Should().BeEmpty();
    }

    [Test]
    public async Task SendingFromCart_IsNoTransition_AndChargesNothing()
    {
        var item = UniqueItem("book");
        var id = Guid.NewGuid();
        await Machines.SaveAsync(Alice, Checkout, id, CheckoutWire("Cart", [item]));

        var sent = await Machines.SendAsync(Alice, Checkout, id);

        sent.ProblemCode.Should().Be("no-transition", "only a Review draft can run the charge");
        Charges.For(item).Should().BeEmpty();
        (await Machines.LoadAsync(Alice, Checkout, id)).State.Should().Be("Cart");
    }
}
