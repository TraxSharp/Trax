using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Bakery.Api.Auth;

namespace Trax.Samples.Bakery.Trains;

/// <summary>What the order's <c>Paying</c> state hands the payment: how many loaves, from the order's context.</summary>
public sealed record PaymentInput
{
    public required long Loaves { get; init; }
}

/// <summary>What the payment answers: whether it went through. The order's outcomes read <c>approved</c>.</summary>
public sealed record PaymentOutput(bool Approved);

public interface ITakePaymentTrain : IServiceTrain<PaymentInput, PaymentOutput>;

/// <summary>
/// Takes payment for an order, a stand-in for a card processor: an order of up to a dozen loaves goes through, a larger
/// one is declined. The order's <c>Paying</c> state invokes it for the customer who placed the order, so it asks for
/// the customer role, as <c>Place</c> does: the host compares the two at startup.
/// </summary>
[TraxAuthorize(Roles = BakeryRoles.Customer)]
public class TakePaymentTrain : ServiceTrain<PaymentInput, PaymentOutput>, ITakePaymentTrain
{
    /// <summary>The most loaves one payment takes.</summary>
    public const long Limit = 12;

    protected override Task<Either<Exception, PaymentOutput>> Junctions() =>
        Chain<ChargeCard>().Resolve();
}

/// <summary>Charges the card, or declines an order over <see cref="TakePaymentTrain.Limit"/>.</summary>
public class ChargeCard : EffectJunction<PaymentInput, PaymentOutput>
{
    public override Task<PaymentOutput> Run(PaymentInput input) =>
        Task.FromResult(new PaymentOutput(input.Loaves <= TakePaymentTrain.Limit));
}
