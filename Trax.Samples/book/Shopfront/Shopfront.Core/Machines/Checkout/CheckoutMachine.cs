using System.Text.Json.Nodes;
using Shopfront.Auth;
using Shopfront.Machines.Checkout.Models;
using Shopfront.Machines.Parcel;
using Shopfront.Machines.Parcel.Models;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Shopfront.Machines.Checkout;

[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    private static readonly Rule HasLines = Field((CheckoutContext c) => c.Lines).CountAtLeast(1);

    private static readonly Lazy<string?> EmbeddedCorpus = new(() =>
    {
        using var stream = typeof(CheckoutMachine).Assembly.GetManifestResourceStream(
            "machines/checkout/differential.json"
        );
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    });

    // The committed corpus, embedded in the library, so the machine can replay it and check itself.
    public override string? Corpus => EmbeddedCorpus.Value;

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(1)
            .StartsAt(CheckoutState.Cart, () => new JsonObject())
            .Start<CheckoutStart>(start => start.Reduce(Keep()).Requires(Roles.Customer))
            .Get(read => read.OwnerOnly())
            .Watch(read => read.OwnerOnly())
            .Discard(read => read.OwnerOnly());

        m.In(CheckoutState.Cart)
            .Context<CheckoutContext>()
            .After(TimeSpan.FromHours(2))
            .GoTo(CheckoutState.Abandoned)
            .On(CheckoutTrigger.SetLines)
            .AnyAuthenticated()
            .WithInput<LinesInput>()
            .Reduce(Set((CheckoutContext c) => c.Lines).FromInput((LinesInput i) => i.Lines))
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.SetAddress)
            .AnyAuthenticated()
            .WithInput<AddressInput>()
            .When(All(HasLines, Input((AddressInput i) => i.Address).NonEmpty()))
            .Because("Put something in the cart and give an address to send it to.")
            .Reduce(Set((CheckoutContext c) => c.Address).FromInput((AddressInput i) => i.Address))
            .To(CheckoutState.Address);

        m.In(CheckoutState.Address)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.ChoosePayment)
            .AnyAuthenticated()
            .WithInput<PaymentInput>()
            .When(Input((PaymentInput i) => i.PaymentToken).NonEmpty())
            .Because("Choose how to pay.")
            .Reduce(
                Set((CheckoutContext c) => c.PaymentToken)
                    .FromInput((PaymentInput i) => i.PaymentToken)
            )
            .To(CheckoutState.Payment);

        m.In(CheckoutState.Payment)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Address)
            .On(CheckoutTrigger.PlaceOrder)
            .Requires(Roles.Customer)
            .To(CheckoutState.Placing);

        // Entering Placing runs PlaceOrder with what the checkout gathered, and its end moves the checkout.
        m.In(CheckoutState.Placing)
            .Context<CheckoutContext>()
            .Invokes<IPlaceOrderTrain, PlaceOrderInput, PlaceOrderOutput>(OrderFor)
            .OnDone(
                CheckoutState.Placed,
                reduce: Set((PlacedContext c) => c.OrderId)
                    .FromInput((PlaceOrderOutput o) => o.OrderId)
            )
            .OnFailed(CheckoutState.Failed)
            .OnCancelled(CheckoutState.Failed);

        m.In(CheckoutState.Failed)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Retry)
            .Requires(Roles.Customer)
            .To(CheckoutState.Placing);

        m.In(CheckoutState.Placed)
            .Context<PlacedContext>()
            .On(CheckoutTrigger.Track)
            .AnyAuthenticated()
            .To(CheckoutState.Shipping);

        // Entering Shipping starts the parcel's machine, and its end moves the checkout.
        m.In(CheckoutState.Shipping)
            .Context<ShippingContext>()
            .Spawns<ParcelMachine, ShippingContext, ParcelResult>(c => c.ParcelId)
            .OnDone(CheckoutState.Delivered)
            .OnFailed(CheckoutState.Placed)
            .OnCancelled(CheckoutState.Placed);

        m.In(CheckoutState.Delivered).Context<ShippingContext>();

        m.In(CheckoutState.Abandoned)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.StartOver)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(CheckoutState.Cart);

        m.Differential(d =>
            d.Sample(
                    CheckoutTrigger.SetLines,
                    new LinesInput { Lines = [new CartLine { Sku = "MUG", Quantity = 2 }] }
                )
                .Sample(CheckoutTrigger.SetAddress, new AddressInput { Address = "1 Main St" })
                .Sample(CheckoutTrigger.SetAddress, new AddressInput())
                .Sample(
                    CheckoutTrigger.ChoosePayment,
                    new PaymentInput { PaymentToken = "tok_visa" }
                )
                .EmptySample(CheckoutTrigger.ChoosePayment)
                .OutcomeSample(
                    CheckoutState.Placing,
                    new PlaceOrderOutput(
                        Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89"),
                        24.00m,
                        "tx_sample"
                    )
                )
        );
    }

    // The order the checkout places: its lines and its payment token. The train prices it.
    private static PlaceOrderInput OrderFor(JsonObject checkout) =>
        new()
        {
            Lines =
            [
                .. checkout["lines"]!
                    .AsArray()
                    .Select(line => new OrderLine(
                        line!["sku"]!.GetValue<string>(),
                        line["quantity"]!.GetValue<int>()
                    )),
            ],
            PaymentToken = checkout["paymentToken"]!.GetValue<string>(),
        };
}
