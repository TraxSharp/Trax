using Microsoft.Extensions.Logging.Abstractions;

namespace Trax.Samples.StateMachine.Tests.UnitTests;

/// <summary>
/// The checkout's effect is the only thing that takes a payment: <see cref="LoggingCharge"/> charges the
/// stored draft's total at the provider for the draft's own user, and <c>listCharges</c> hands each caller
/// back only their own charges.
/// </summary>
public class PaymentTests
{
    private sealed class FixedPrincipal(string? key) : ISnapshotPrincipal
    {
        public string? CurrentUserKey => key;
    }

    private static Snapshot Review(params string[] items) =>
        new()
        {
            Machine = "checkout",
            Version = 2,
            State = "Review",
            Context = new JsonObject
            {
                ["items"] = new JsonArray(
                    items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()
                ),
                ["receipt"] = null,
                ["total"] = items.Length * CheckoutMachine.UnitPriceCents,
            },
        };

    [Test]
    public async Task The_charge_takes_the_stored_total_from_the_drafts_user_and_returns_the_providers_receipt()
    {
        var provider = new SimulatedPaymentProvider();
        var charge = new LoggingCharge(
            provider,
            new FixedPrincipal("TraxApiKey:alice"),
            NullLogger<LoggingCharge>.Instance
        );

        var receipt = await charge.Run(Review("book", "pen"));

        var taken = provider.ChargesFor("TraxApiKey:alice").Should().ContainSingle().Subject;
        taken.Receipt.Should().Be(receipt);
        taken.AmountCents.Should().Be(1998);
        taken.Items.Should().Be(2);
        provider.ChargesFor("TraxApiKey:bob").Should().BeEmpty();
    }

    [Test]
    public async Task The_charge_refuses_to_run_for_no_one()
    {
        var charge = new LoggingCharge(
            new SimulatedPaymentProvider(),
            new FixedPrincipal(null),
            NullLogger<LoggingCharge>.Instance
        );

        var act = () => charge.Run(Review("book"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task ListCharges_returns_only_the_callers_charges_newest_first()
    {
        var provider = new SimulatedPaymentProvider();
        var first = provider.Charge("TraxApiKey:alice", 999, 1);
        var second = provider.Charge("TraxApiKey:alice", 1998, 2);
        provider.Charge("TraxApiKey:bob", 999, 1);
        var output = await new ListChargesJunction(
            provider,
            new FixedPrincipal("TraxApiKey:alice")
        ).Run(new ListChargesInput());

        output.Charges.Select(c => c.Receipt).Should().Equal(second.Receipt, first.Receipt);
    }
}
